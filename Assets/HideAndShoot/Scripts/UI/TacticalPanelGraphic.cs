using UnityEngine;
using UnityEngine.UI;

namespace HideAndShoot.UI
{
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TacticalPanelGraphic : MaskableGraphic
    {
        public Color borderColor=new Color(.22f,.36f,.40f,.8f);
        public float corner=12f;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect=rectTransform.rect;
            float cut=Mathf.Min(corner,Mathf.Min(rect.width,rect.height)*.25f);
            var outer=new[] {
                new Vector2(rect.xMin+cut,rect.yMin),new Vector2(rect.xMax-cut,rect.yMin),
                new Vector2(rect.xMax,rect.yMin+cut),new Vector2(rect.xMax,rect.yMax-cut),
                new Vector2(rect.xMax-cut,rect.yMax),new Vector2(rect.xMin+cut,rect.yMax),
                new Vector2(rect.xMin,rect.yMax-cut),new Vector2(rect.xMin,rect.yMin+cut)
            };
            for(int i=0;i<8;i++)mesh.AddVert(outer[i],borderColor,Vector2.zero);
            for(int i=0;i<8;i++)
            {
                var point=outer[i];
                point.x=Mathf.MoveTowards(point.x,rect.center.x,1.2f);
                point.y=Mathf.MoveTowards(point.y,rect.center.y,1.2f);
                mesh.AddVert(point,color,Vector2.zero);
            }
            for(int i=0;i<8;i++)
            {
                int next=(i+1)%8;
                mesh.AddTriangle(i,next,i+8);
                mesh.AddTriangle(next,next+8,i+8);
            }
            for(int i=1;i<7;i++)mesh.AddTriangle(8,8+i,8+i+1);
        }
    }
}
