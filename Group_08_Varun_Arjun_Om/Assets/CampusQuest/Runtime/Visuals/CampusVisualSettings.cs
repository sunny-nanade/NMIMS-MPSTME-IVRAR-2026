using UnityEngine;
namespace CampusQuest {
[CreateAssetMenu(menuName="Campus Quest/Visual Settings")]
public class CampusVisualSettings:ScriptableObject {
 public bool doorsStartOpen=true;
 public bool addComputersToGenericLabs=true;
 public bool showSamplePosters=true;
 [Range(128,1024)]public int textureResolution=512;
 [TextArea]public string accuracyNote="Dimensions, surface finishes, furniture and poster content are approximate or placeholders. Room footprints follow existing source layouts. Canteen is open to the atrium, confirmed by user.";
}
}
