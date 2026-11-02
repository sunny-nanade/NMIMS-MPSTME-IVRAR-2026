using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace CampusQuest {
[Serializable] public class AdditionalDoorData { public string side; public float offset=.5f,width=1.6f; }
[Serializable] public class RoomData {
 public string id, label, category, description;
 public float x,z,width,depth;
 public string doorSide="S";
 public float doorOffset=.5f, doorWidth=1.2f;
 public float[] extraDoorOffsets=new float[0];
 public AdditionalDoorData[] additionalDoors=new AdditionalDoorData[0];
 public bool isVerified, isPlaceholder=true, placementApproximate=true;
 public Rect Bounds => new Rect(x,z,width,depth);
}
[Serializable] public class StairData { public string id; public float x,z; public float depth=10; public bool entryEast; public string entrySide; public bool reverse; }
[Serializable] public class LiftData { public string id; public float x,z; public bool northFacing; }
[Serializable] public class RectData { public float x=11,z=21,width=18,depth=14; public Rect Bounds => new Rect(x,z,width,depth); }
[Serializable] public class LayoutData {
 public string id, evidence;
 public float width=40, depth=56, floorHeight=3.6f, wallHeight=3, wallThickness=.15f;
 public bool dimensionsApproximate=true, hasAtrium=true;
 public RectData atrium=new RectData();
 public RectData[] ceilingOpenings=new RectData[0];
 public RoomData[] rooms;
 public StairData[] stairs;
 public LiftData[] lifts;
}
[CreateAssetMenu(menuName="Campus Quest/Floor Config")]
public class FloorConfig : ScriptableObject {
 public int layoutRevision;
 public int level; // C=-1, G=0, upper floors 1..8; never a scene index
 public string sceneName, displayName;
 public TextAsset layoutJSON;
 public RoomData[] rooms;
 public bool layoutApproximate=true;
 public string[] excludedRoomIDs=new string[0];
 public RoomData[] ResolvedRooms(){
  var result=new List<RoomData>();
  foreach(var shared in ReadLayout().rooms){
   if(excludedRoomIDs.Contains(shared.id))continue;
   var item=JsonUtility.FromJson<RoomData>(JsonUtility.ToJson(shared));
   var replacement=rooms.FirstOrDefault(r=>r.id==item.id);
   if(replacement!=null){item.label=replacement.label;item.category=replacement.category;item.description=replacement.description;item.isVerified=replacement.isVerified;item.isPlaceholder=replacement.isPlaceholder;}
   result.Add(item);
  }
  foreach(var extra in rooms)if(!result.Any(r=>r.id==extra.id)&&!excludedRoomIDs.Contains(extra.id))result.Add(extra);
  return result.ToArray();
 }
 public LayoutData ReadLayout() => JsonUtility.FromJson<LayoutData>(layoutJSON.text);
 public static string SceneFor(int level) => level==-1 ? "Scene_09_Canteen" : level==0 ? "Scene_00_GroundFloor" : "Scene_"+level.ToString("00")+"_Floor"+level;
 public static string Label(int level) => level==-1 ? "C" : level==0 ? "G" : level.ToString();
}
}
