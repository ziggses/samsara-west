using SamsaraWest.Core;
using SamsaraWest.Localization;
using TMPro;
using UnityEngine;

namespace SamsaraWest.UI
{
    /// <summary>
    /// 文本接线样例：界面只认文本键，任何组件都不写中文字面量（任务书 FND-07）。
    /// 服务未就绪时显示键本身，让遗漏在开发期就肉眼可见，而不是静默空白。
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public sealed class LocalizedLabel : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("文本键，例如 ui.battle.command.attack。不是中文原文。")]
        private string _key;

        [SerializeField] private bool _refreshOnEnable = true;

        private TMP_Text _label;
        private ILocalizationService _localization;

        public string Key => _key;

        private void Awake()
        {
            _label = GetComponent<TMP_Text>();
        }

        private void OnEnable()
        {
            if (_refreshOnEnable)
            {
                Refresh();
            }
        }

        /// <summary>切换文本键并立即刷新，供 UI 状态机调用。</summary>
        public void SetKey(string key)
        {
            _key = key;
            Refresh();
        }

        public void Refresh()
        {
            if (_label == null)
            {
                _label = GetComponent<TMP_Text>();
            }

            if (string.IsNullOrEmpty(_key))
            {
                _label.text = string.Empty;
                return;
            }

            if (_localization == null)
            {
                if (!GameServices.IsReady || !GameServices.Registry.TryResolve(out _localization))
                {
                    // 刻意显示键名：本地化服务缺失时必须看得见，不能悄悄显示空白。
                    _label.text = $"[[{_key}]]";
                    return;
                }
            }

            _label.text = _localization.Get(_key);
        }
    }
}
