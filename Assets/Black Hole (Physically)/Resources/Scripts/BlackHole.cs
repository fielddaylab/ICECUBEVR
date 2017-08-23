using UnityEngine;

namespace BHP
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(Camera))]
    public class BlackHole : MonoBehaviour
    {
        public GameObject balckHole;
        public float radius = 1;
        public float waves = 0.5f;
        public float deformationPowerIN = 0.7f;
        public float deformationPowerOUT = 2f;

        private Material _material;
        protected Material material
        {
            get
            {
                if (_material == null)
                {
                    _material = new Material(Shader.Find("Tauri Interactive/Black Hole (Physically)"));
                    _material.hideFlags = HideFlags.HideAndDontSave;
                }
                return _material;
            }
        }

        protected virtual void OnDisable()
        {
            if (_material)
            {
                DestroyImmediate(_material);
            }
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (this.GetComponent<Camera>().WorldToScreenPoint(balckHole.transform.position).z > 0)
                if (material)
                {
                    Vector2 pos = new Vector2(
                        this.GetComponent<Camera>().WorldToScreenPoint(balckHole.transform.position).x / this.GetComponent<Camera>().pixelWidth,
                        1 - this.GetComponent<Camera>().WorldToScreenPoint(balckHole.transform.position).y / this.GetComponent<Camera>().pixelHeight);

                    material.SetVector("_Position", new Vector2(pos.x, pos.y));
                    material.SetFloat("_Ratio", ((float)Screen.width / Screen.height));
                    material.SetFloat("_Waves", waves);
                    material.SetFloat("_Rad", radius);
                    material.SetFloat("_DefPowerIN", deformationPowerIN);
                    material.SetFloat("_DefPowerOUT", deformationPowerOUT);
                    material.SetFloat("_Distance", Vector3.Distance(balckHole.transform.position, this.transform.position));
                    Graphics.Blit(source, destination, material);
                }
        }
    }
}