using UnityEngine;
namespace CampusQuest {
[CreateAssetMenu(menuName="Campus Quest/Service Catalog")]
public class CampusServiceCatalog:ScriptableObject {
 public string[] books={"Introduction to Programming","Engineering Mathematics","Project Design Handbook"};
 public string[] components={"Breadboard kit","Resistor kit","Sensor kit"};
 public string document="Sample project notes";
 public string recipient="Dr. Sunny Nanade";
 [TextArea] public string disclaimer="Practice simulation: titles, components and issue procedure are examples. Confirm actual borrowing, printing and submission rules with campus staff.";
 public bool sampleContent=true;
 }
}
