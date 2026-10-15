using UnityEngine;
namespace HideAndShoot.Combat
{
    // Visual only: server still controls hits and ammunition.
    public sealed class PistolLaserTracer : MonoBehaviour
    {
        private LineRenderer beam;
        private Material material;
        private Vector3 from;
        private Vector3 to;
        private float born;
        private const float Duration=.22f;

        public static void Show(Vector3 origin,Vector3 endpoint)
        {
            if((endpoint-origin).sqrMagnitude<.000001f)return;
            var go=new GameObject("Local_PistolLaserTracer");
            go.AddComponent<PistolLaserTracer>().Begin(origin,endpoint);
        }

        private void Begin(Vector3 origin,Vector3 endpoint)
        {
            from=origin;
            to=endpoint;
            born=Time.unscaledTime;
            beam=gameObject.AddComponent<LineRenderer>();
            beam.useWorldSpace=true;
            beam.positionCount=2;
            beam.numCapVertices=4;
            beam.alignment=LineAlignment.View;
            beam.widthMultiplier=.075f;
            beam.startColor=new Color(1f,1f,.7f,1f);
            beam.endColor=new Color(.28f,.9f,1f,.95f);
            var shader=Shader.Find("Sprites/Default");
            if(shader==null)shader=Shader.Find("Universal Render Pipeline/Unlit");
            if(shader!=null)
            {
                material=new Material(shader);
                beam.sharedMaterial=material;
            }
            Render(0f);
        }

        private void Update()
        {
            float t=(Time.unscaledTime-born)/Duration;
            if(t>=1f){Destroy(gameObject);return;}
            Render(Mathf.Clamp01(t));
        }

        private void Render(float t)
        {
            beam.SetPosition(0,from);
            beam.SetPosition(1,to);
            beam.widthMultiplier=Mathf.Lerp(.012f,.003f,t);
            beam.startColor=new Color(1f,1f,.7f,1f-t);
            beam.endColor=new Color(.28f,.9f,1f,1f-t);
        }
        private void OnDestroy()
        {
            if(material==null)return;
            if(Application.isPlaying)Destroy(material);
            else DestroyImmediate(material);
        }
    }
}
