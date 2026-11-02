using UnityEngine;
namespace CampusQuest {
[RequireComponent(typeof(TextMesh))]
public class CampusTextAtlasSync:MonoBehaviour {
 TextMesh label;
 void OnEnable(){label=GetComponent<TextMesh>();Font.textureRebuilt+=Refresh;Refresh(label.font);}
 void OnDisable(){Font.textureRebuilt-=Refresh;}
 void Refresh(Font font){if(label==null||font==null||label.font!=font)return;var r=GetComponent<Renderer>();if(r!=null&&r.sharedMaterial!=null)r.sharedMaterial.mainTexture=font.material.mainTexture;}
}
}
