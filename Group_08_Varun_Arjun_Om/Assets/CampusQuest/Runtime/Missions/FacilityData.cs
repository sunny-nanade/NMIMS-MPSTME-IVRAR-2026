using System.Linq;
using UnityEngine;
namespace CampusQuest {
[CreateAssetMenu(menuName="Campus Quest/Facility")]
public class FacilityData:ScriptableObject {
 public string facilityID, fallbackName, roomID;
 public FloorConfig floor;
 public bool isVerified, isPlaceholder;
 [TextArea] public string learningText;
 public RoomData Room=>floor==null?null:floor.ResolvedRooms().FirstOrDefault(r=>r.id==roomID);
 public string DisplayName=>Room!=null?Room.label:fallbackName;
 public int Level=>floor==null?0:floor.level;
 public bool Verified=>isVerified&&!isPlaceholder&&(string.IsNullOrEmpty(roomID)||(Room!=null&&Room.isVerified&&!Room.isPlaceholder));
}
}
