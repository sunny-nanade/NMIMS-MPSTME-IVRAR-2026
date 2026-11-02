using UnityEngine;
namespace CampusQuest {
[CreateAssetMenu(menuName="Campus Quest/Menu Item")]
public class MenuItemData : ScriptableObject {
 public string itemID, displayName;
 public int priceINR;
 public bool isVerified, isPlaceholder=true;
}
}
