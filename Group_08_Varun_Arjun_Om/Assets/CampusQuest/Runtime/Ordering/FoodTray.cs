using UnityEngine;
namespace CampusQuest {
public class FoodTray : MonoBehaviour {
 public GameObject visual;
 public TextMesh label;
 void Update(){var m=CanteenOrderManager.Instance;bool show=m!=null&&m.Step==OrderStep.TrayReady;if(visual!=null&&visual.activeSelf!=show)visual.SetActive(show);if(show&&label!=null)label.text=(m.SelectedItem==null?"Food tray":m.SelectedItem.displayName)+"\nE — collect tray";}
 public void Take(){if(CanteenOrderManager.Instance!=null)CanteenOrderManager.Instance.TakeTray();}
}
}
