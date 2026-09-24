using System.Collections.Generic;
using System.Text;
using SamsaraWest.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SamsaraWest.UI
{
    /// <summary>
    /// 运行期异常面板（FND-08）。它把自己挂成 GameLog 的一个 sink，
    /// 于是「日志里记过的错」和「屏幕上看到的错」永远是同一份数据，不用两头对。
    /// 只在编辑器与 Development Build 生效；发布版会自动降级为纯文件日志。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ErrorOverlay : MonoBehaviour, ILogSink
    {
        [SerializeField] private LogLevel _minLevel = LogLevel.Error;
        [SerializeField] [Range(1, 32)] private int _maxEntries = 8;

        private static ErrorOverlay _instance;
        private readonly List<LogRecord> _records = new List<LogRecord>(16);
        private Vector2 _scroll;
        private bool _visible;

        public static ErrorOverlay Instance => _instance;

        public bool IsVisible => _visible;

        public IReadOnlyList<LogRecord> Records => _records;

        /// <summary>发布版（非 Development Build）不显示面板。</summary>
        public static bool IsSupportedInThisBuild => Application.isEditor || Debug.isDebugBuild;

        /// <summary>
        /// 自挂到运行期：如果还得靠场景接线把它带起来，那么接线漏挂时它恰好不会出现 ——
        /// 而那一刻正是最需要看到错误的时候。发布版不创建，避免留一个不画任何东西的对象。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstall()
        {
            if (!IsSupportedInThisBuild)
            {
                return;
            }

            // 场景切换是「谁还在」的天然检查点：面板不随场景销毁，但万一被外力清掉
            // （测试会重置日志，对象也可能被误删），下一次加载场景时自己长回来。
            // 一个要等人发现才会出现的错误面板，等于没有面板。
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureCreated();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureCreated();
        }

        /// <summary>
        /// 幂等地返回一个「活着且已接线」的面板。光有实例还不够：它是 GameLog 的一个 sink，
        /// 一旦 sink 被清掉（<see cref="GameLog.Reset"/>）而实例还在，它就是一个画不出东西的壳，
        /// 「日志里记过的错」与「屏幕上看到的错」重新变成两份数据 —— 那正是本组件要修的病根。
        /// </summary>
        public static ErrorOverlay EnsureCreated()
        {
            if (_instance != null)
            {
                _instance.AttachSinkIfSupported();
                return _instance;
            }

            var host = new GameObject("ErrorOverlay");
            DontDestroyOnLoad(host);
            return host.AddComponent<ErrorOverlay>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            AttachSinkIfSupported();
        }

        private void AttachSinkIfSupported()
        {
            if (IsSupportedInThisBuild)
            {
                GameLog.AddSink(this);
            }
            else
            {
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            GameLog.RemoveSink(this);
            if (_instance == this)
            {
                _instance = null;
            }
        }

        public void Write(in LogRecord record)
        {
            if (record.Level < _minLevel)
            {
                return;
            }

            _records.Add(record);
            while (_records.Count > _maxEntries)
            {
                _records.RemoveAt(0);
            }

            _visible = true;
        }

        public void Clear()
        {
            _records.Clear();
            _visible = false;
        }

        public string BuildText()
        {
            var builder = new StringBuilder(_records.Count * 128);
            for (var i = 0; i < _records.Count; i++)
            {
                builder.AppendLine(_records[i].ToString());
            }

            return builder.ToString();
        }

        private void OnGUI()
        {
            if (!_visible || _records.Count == 0)
            {
                return;
            }

            const float width = 560f;
            var height = Mathf.Min(240f, 44f + _records.Count * 34f);
            var area = new Rect((Screen.width - width) * 0.5f, 8f, width, height);
            GUI.Box(area, $"运行期异常（最近 {_records.Count} 条）");

            var inner = new Rect(area.x + 8f, area.y + 24f, area.width - 16f, area.height - 60f);
            _scroll = GUI.BeginScrollView(inner, _scroll, new Rect(0f, 0f, inner.width - 20f, _records.Count * 34f));
            for (var i = 0; i < _records.Count; i++)
            {
                var record = _records[i];
                GUI.Label(new Rect(0f, i * 34f, inner.width - 20f, 32f), $"[{record.Channel}] {record.Message}\n{(string.IsNullOrEmpty(record.Context) ? string.Empty : record.Context)}");
            }

            GUI.EndScrollView();

            if (GUI.Button(new Rect(area.x + 8f, area.yMax - 28f, 80f, 22f), "复制"))
            {
                GUIUtility.systemCopyBuffer = BuildText();
            }

            if (GUI.Button(new Rect(area.x + 94f, area.yMax - 28f, 80f, 22f), "清空"))
            {
                Clear();
            }
        }
    }
}
