using System;
using UnityEngine;
namespace CampusQuest {
[Serializable]public class AtriumContextLevel {public int level;public GameObject geometry;}
public class AtriumContextController:MonoBehaviour {
 public static AtriumContextController Instance {get;private set;}
 public AtriumContextLevel[] levels=new AtriumContextLevel[0];
 public float floorHeight=3.6f;
 int shown=int.MinValue;
 void Awake(){Instance=this;SetLevel(0);}
 public void SetLevel(int current){
 shown=current;
 // Active gameplay scenes use floor-local Y=0. Offset the visual tower to match.
 transform.localPosition=new Vector3(0,-(current+1)*floorHeight,0);
 foreach(var item in levels)if(item.geometry!=null)item.geometry.SetActive(item.level!=current);
 }
 void LateUpdate(){var tm=SceneTransitionManager.Instance;if(tm!=null&&shown!=tm.CurrentLevel)SetLevel(tm.CurrentLevel);}
 void OnDestroy(){if(Instance==this)Instance=null;}
}
}
