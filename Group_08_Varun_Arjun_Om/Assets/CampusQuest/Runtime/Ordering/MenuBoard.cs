using UnityEngine;
namespace CampusQuest {
public class MenuBoard : MonoBehaviour {
 public void Open(){var m=CanteenOrderManager.Instance;if(m==null){Debug.LogError("Open Core first: canteen manager missing.");return;}if(!m.CanInteract)return;if(m.Step!=OrderStep.ChooseDish){m.Hint(m.Objective);return;}DesktopHUD.Instance.ShowMenuBoard(this);}
 public void Select(MenuItemData item){var m=CanteenOrderManager.Instance;if(m!=null)m.SelectDish(item);DesktopHUD.Instance.CloseMenus();}
}
}
