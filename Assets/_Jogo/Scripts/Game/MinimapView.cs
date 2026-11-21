using UnityEngine;

namespace Entregador.Game
{
    /// <summary>Minimapa: câmera ortográfica olhando para baixo, seguindo a moto, desenhada numa RenderTexture.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class MinimapView : MonoBehaviour
    {
        [SerializeField] private ArcadeBikeController _bike;
        [SerializeField] private float _orthoSize = 120f;
        [SerializeField] private float _height = 150f;
        [SerializeField] private int _resolution = 256;

        private Camera _camera;
        private RenderTexture _texture;

        public RenderTexture Texture => _texture;

        public void Configure(ArcadeBikeController bike)
        {
            _bike = bike;
        }

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = _orthoSize;
            _texture = new RenderTexture(_resolution, _resolution, 16);
            _texture.name = "MinimapaRT";
            _camera.targetTexture = _texture;
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private void LateUpdate()
        {
            if (_bike != null)
            {
                Vector3 p = _bike.Position;
                transform.position = new Vector3(p.x, _height, p.z);
                transform.rotation = Quaternion.Euler(90f, _bike.Yaw, 0f);
            }
        }

        private void OnDestroy()
        {
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
        }
    }
}
