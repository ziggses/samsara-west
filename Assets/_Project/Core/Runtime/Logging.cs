using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace SamsaraWest.Core
{
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warning = 3,
        Error = 4,
        Fatal = 5,
        Off = 100,
    }

    /// <summary>
    /// 日志频道。与模块一一对应，用于「能追踪战斗、任务、存档错误」这条验收标准。
    /// </summary>
    public enum LogChannel
    {
        Core = 0,
        Data = 1,
        Flow = 2,
        Battle = 3,
        Exploration = 4,
        Narrative = 5,
        Progression = 6,
        Economy = 7,
        UI = 8,
        Save = 9,
        Localization = 10,
        Audio = 11,
        Tools = 12,
    }

    /// <summary>结构化日志记录。不可变，避免环形缓冲区里被后续写入污染。</summary>
    public readonly struct LogRecord
    {
        public LogRecord(long sequence, double elapsedSeconds, DateTime utc, LogLevel level, LogChannel channel, string message, string context, string stackTrace)
        {
            Sequence = sequence;
            ElapsedSeconds = elapsedSeconds;
            Utc = utc;
            Level = level;
            Channel = channel;
            Message = message;
            Context = context;
            StackTrace = stackTrace;
        }

        public long Sequence { get; }

        public double ElapsedSeconds { get; }

        public DateTime Utc { get; }

        public LogLevel Level { get; }

        public LogChannel Channel { get; }

        public string Message { get; }

        /// <summary>可选上下文，例如实体 ID、战斗局内编号。</summary>
        public string Context { get; }

        public string StackTrace { get; }

        public bool IsFailure => Level >= LogLevel.Error;

        public override string ToString()
        {
            var context = string.IsNullOrEmpty(Context) ? string.Empty : $" [{Context}]";
            return $"[{ElapsedSeconds,9:F3}] {Level,-7} {Channel,-13}{context} {Message}";
        }
    }

    public interface ILogSink
    {
        void Write(in LogRecord record);
    }

    /// <summary>固定容量的环形缓冲。用于运行期错误面板与现场取证，永不增长。</summary>
    public sealed class RingBufferSink : ILogSink
    {
        private readonly LogRecord[] _buffer;
        private int _next;
        private int _count;

        public RingBufferSink(int capacity = 2048)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _buffer = new LogRecord[capacity];
        }

        public int Capacity => _buffer.Length;

        public int Count => _count;

        /// <summary>已写入的总条数（含被覆盖的）。</summary>
        public long TotalWritten { get; private set; }

        public void Write(in LogRecord record)
        {
            _buffer[_next] = record;
            _next = (_next + 1) % _buffer.Length;
            if (_count < _buffer.Length)
            {
                _count++;
            }

            TotalWritten++;
        }

        /// <summary>按时间正序导出当前快照。</summary>
        public LogRecord[] Snapshot()
        {
            var result = new LogRecord[_count];
            var start = _count == _buffer.Length ? _next : 0;
            for (var i = 0; i < _count; i++)
            {
                result[i] = _buffer[(start + i) % _buffer.Length];
            }

            return result;
        }

        public LogRecord[] Snapshot(LogLevel minLevel)
        {
            var all = Snapshot();
            if (minLevel <= LogLevel.Trace)
            {
                return all;
            }

            var filtered = new List<LogRecord>(all.Length);
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i].Level >= minLevel)
                {
                    filtered.Add(all[i]);
                }
            }

            return filtered.ToArray();
        }

        public void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _next = 0;
            _count = 0;
        }
    }

    /// <summary>追加式文件日志。WebGL 平台自动降级为不写入。</summary>
    public sealed class FileLogSink : ILogSink, IDisposable
    {
        private readonly StreamWriter _writer;
        private readonly object _gate = new object();

        public FileLogSink(string directory, string fileNamePrefix = "samsara")
        {
            Directory.CreateDirectory(directory);
            FilePath = Path.Combine(directory, $"{fileNamePrefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
            _writer = new StreamWriter(FilePath, append: true, new UTF8Encoding(false)) { AutoFlush = true };
        }

        public string FilePath { get; }

        public void Write(in LogRecord record)
        {
            var line = $"{record.Utc:O} {record}";
            lock (_gate)
            {
                try
                {
                    _writer.WriteLine(line);
                    if (record.StackTrace != null)
                    {
                        _writer.WriteLine(record.StackTrace);
                    }
                }
                catch (IOException)
                {
                    // 磁盘满或被占用时不得影响游戏运行，静默降级。
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _writer?.Dispose();
            }
        }
    }

    /// <summary>
    /// 分层分频道的日志门面。调用点必须先经过 <see cref="IsEnabled"/> 判断，
    /// 未启用的级别不会发生字符串拼接与堆分配。
    /// </summary>
    public static class GameLog
    {
        public const int DefaultHistoryCapacity = 2048;

        private static readonly object Gate = new object();
        private static readonly List<ILogSink> Sinks = new List<ILogSink>(4);
        private static readonly RingBufferSink HistorySink = new RingBufferSink(DefaultHistoryCapacity);
        private static readonly Stopwatch Uptime = Stopwatch.StartNew();

        private static LogLevel _minLevel = LogLevel.Info;
        private static long _sequence;
        private static bool _defaultsInstalled;
        private static bool _fileSinkEnabled = true;

        /// <summary>运行期日志历史（环形缓冲），供错误面板读取。</summary>
        public static RingBufferSink History => HistorySink;

        public static LogLevel MinLevel => _minLevel;

        public static long TotalCount => _sequence;

        public static bool IsEnabled(LogLevel level) => level >= _minLevel && _minLevel != LogLevel.Off;

        public static void Configure(LogLevel minLevel)
        {
            lock (Gate)
            {
                _minLevel = minLevel;
            }
        }

        public static void AddSink(ILogSink sink)
        {
            if (sink == null)
            {
                return;
            }

            lock (Gate)
            {
                if (!Sinks.Contains(sink))
                {
                    Sinks.Add(sink);
                }
            }
        }

        public static void RemoveSink(ILogSink sink)
        {
            lock (Gate)
            {
                Sinks.Remove(sink);
            }
        }

        /// <summary>清空历史与已注册的额外 sink（不含文件 sink 的释放）。主要供测试使用。</summary>
        public static void Reset()
        {
            lock (Gate)
            {
                Sinks.Clear();
                _sequence = 0;
                _minLevel = LogLevel.Info;
                _defaultsInstalled = false;
            }

            HistorySink.Clear();
        }

        public static void DisableFileSink() => _fileSinkEnabled = false;

        public static void EnsureInstalled()
        {
            if (_defaultsInstalled)
            {
                return;
            }

            _defaultsInstalled = true;
            AddSink(HistorySink);

            if (!_fileSinkEnabled)
            {
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL 下不做文件日志，只保留内存历史。
            return;
#else
            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "Logs");
                AddSink(new FileLogSink(dir));
            }
            catch (Exception exception)
            {
                Write(LogLevel.Warning, LogChannel.Core, $"文件日志初始化失败，已降级为仅内存日志：{exception.Message}");
            }
#endif
        }

        public static LogRecord[] Snapshot(LogLevel minLevel = LogLevel.Trace) => HistorySink.Snapshot(minLevel);

        public static void Trace(LogChannel channel, string message, string context = null) =>
            Write(LogLevel.Trace, channel, message, context);

        public static void Debug(LogChannel channel, string message, string context = null) =>
            Write(LogLevel.Debug, channel, message, context);

        public static void Info(LogChannel channel, string message, string context = null) =>
            Write(LogLevel.Info, channel, message, context);

        public static void Warn(LogChannel channel, string message, string context = null) =>
            Write(LogLevel.Warning, channel, message, context);

        public static void Error(LogChannel channel, string message, string context = null) =>
            Write(LogLevel.Error, channel, message, context);

        public static void Error(LogChannel channel, string message, Exception exception, string context = null) =>
            Write(LogLevel.Error, channel, message, context, exception);

        public static void Fatal(LogChannel channel, string message, Exception exception = null, string context = null) =>
            Write(LogLevel.Fatal, channel, message, context, exception);

        public static void Write(LogLevel level, LogChannel channel, string message, string context = null, Exception exception = null)
        {
            if (!IsEnabled(level))
            {
                return;
            }

            var text = exception == null ? message : $"{message} -> {exception.GetType().Name}: {exception.Message}";
            var stack = level >= LogLevel.Warning
                ? exception?.StackTrace ?? new StackTrace(2, fNeedFileInfo: false).ToString()
                : null;

            LogRecord record;
            lock (Gate)
            {
                record = new LogRecord(
                    ++_sequence,
                    Uptime.Elapsed.TotalSeconds,
                    DateTime.UtcNow,
                    level,
                    channel,
                    text,
                    context,
                    stack);
            }

            ILogSink[] sinks;
            lock (Gate)
            {
                sinks = Sinks.ToArray();
            }

            if (sinks.Length == 0)
            {
                // 未安装 sink 时至少保留内存历史，避免启动早期的日志丢失。
                HistorySink.Write(record);
                return;
            }

            for (var i = 0; i < sinks.Length; i++)
            {
                try
                {
                    sinks[i].Write(record);
                }
                catch (Exception exception1)
                {
                    UnityEngine.Debug.LogWarning($"日志 sink 写入失败：{exception1.Message}");
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void AutoInstall()
        {
            EnsureInstalled();
            UnityEngine.Debug.Log($"[SamsaraWest] 日志系统已就绪，历史容量 {HistorySink.Capacity}。");
        }
    }
}
