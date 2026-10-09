using UnityEngine;

namespace SamsaraWest.Rendering
{
    /// <summary>
    /// 相机跟随。挂在主相机上，把相机挪到目标身上。
    /// </summary>
    /// <remarks>
    /// <b>为什么默认不平滑</b>：像素画里镜头平移一旦插值，摄像头就落在半个像素上，整屏重采样发糊。
    /// 默认 <c>_smoothTime = 0</c>，镜头整像素跳动；要电影感的镜头再自己调大。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CameraFollowRig : MonoBehaviour
    {
        public const float DefaultDepth = -10f;

        [SerializeField]
        [Tooltip("平滑时间。像素画默认 0，插值会让像素重采样糊掉。")]
        private float _smoothTime;

        [SerializeField]
        [Tooltip("相机在 z 上的位置。正交相机靠它把画面推到其它物体之前。")]
        private float _depth = DefaultDepth;

        private Transform _target;
        private Vector3 _velocity;
        private bool _snapPending = true;

        public Transform Target => _target;

        public bool HasTarget => _target != null;

        /// <summary>当前聚焦的世界坐标。</summary>
        public Vector3 Focus { get; private set; }

        public float SmoothTime
        {
            get => _smoothTime;
            set => _smoothTime = value;
        }

        public float Depth
        {
            get => _depth;
            set => _depth = value;
        }

        /// <summary>换目标。换完下一帧直接吸附——不吸附镜头会从上一张图的位置一路滑过去，像镜头飞了。</summary>
        public void Follow(Transform target)
        {
            if (_target == target)
            {
                return;
            }

            _target = target;
            _snapPending = true;
        }

        public void Clear()
        {
            _target = null;
        }

        /// <summary>立刻把相机放到世界坐标处。</summary>
        public void SnapTo(Vector3 worldPosition)
        {
            Focus = worldPosition;
            transform.position = new Vector3(worldPosition.x, worldPosition.y, _depth);
            _velocity = Vector3.zero;
            _snapPending = false;
        }

        /// <summary>取主相机上的跟随器；还没挂就挂一个。</summary>
        public static CameraFollowRig Ensure(Camera camera)
        {
            if (camera == null)
            {
                return null;
            }

            var rig = camera.GetComponent<CameraFollowRig>();
            if (rig != null)
            {
                return rig;
            }

            rig = camera.gameObject.AddComponent<CameraFollowRig>();
            var existingZ = camera.transform.position.z;
            rig.Depth = Mathf.Approximately(existingZ, 0f) ? DefaultDepth : existingZ;
            return rig;
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                return;
            }

            var focus = _target.position;
            if (_snapPending || _smoothTime <= 0f)
            {
                SnapTo(focus);
                return;
            }

            Focus = focus;
            var wanted = new Vector3(focus.x, focus.y, _depth);
            transform.position = Vector3.SmoothDamp(transform.position, wanted, ref _velocity, _smoothTime);
        }
    }
}
