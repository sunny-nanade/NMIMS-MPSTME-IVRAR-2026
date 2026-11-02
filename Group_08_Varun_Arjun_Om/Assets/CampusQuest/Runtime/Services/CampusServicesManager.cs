using UnityEngine;
namespace CampusQuest {
public class CampusServicesManager:MonoBehaviour {
 public static CampusServicesManager Instance{get;private set;}
 public CampusServiceCatalog catalog;
 public CampusServiceState State{get;private set;}=new CampusServiceState();
 float printSeconds;
 GameObject carriedPaper;public Material paperMaterial;
 void Awake(){Instance=this;}
 void OnDestroy(){if(carriedPaper!=null)Destroy(carriedPaper);if(Instance==this)Instance=null;}
 public void ResetSession(){State=new CampusServiceState();printSeconds=0;}
 public string Report=>"Book borrowed: "+(State.bookCollected?catalog.books[State.book]:"Not completed")+"\nComponent borrowed: "+(State.componentCollected?catalog.components[State.component]:"Not completed")+"\nPrintout: "+(State.delivered?"Delivered to "+catalog.recipient:State.printCollected?"Collected, awaiting delivery":"Not delivered")+"\n"+catalog.disclaimer;
 void LateUpdate(){
 var hud=DesktopHUD.Instance;
 if(State.printCollected&&carriedPaper==null&&hud!=null&&hud.player!=null&&hud.player.view!=null){
 carriedPaper=GameObject.CreatePrimitive(PrimitiveType.Cube);carriedPaper.name="Carried project notes — desktop representation";Destroy(carriedPaper.GetComponent<Collider>());carriedPaper.layer=2;
 carriedPaper.transform.SetParent(hud.player.view.transform,false);carriedPaper.transform.localPosition=new Vector3(.3f,-.25f,.65f);carriedPaper.transform.localRotation=Quaternion.Euler(15,-15,0);carriedPaper.transform.localScale=new Vector3(.21f,.29f,.008f);
 if(paperMaterial!=null)carriedPaper.GetComponent<Renderer>().sharedMaterial=paperMaterial;
 }
 if(carriedPaper!=null)carriedPaper.SetActive(State.printCollected);
 }
 void OnGUI(){
 var hud=DesktopHUD.Instance;var m=MissionManager.Instance;if(hud==null||hud.MenuOpen||m==null||!m.Started||m.Finished)return;
 string inventory=(State.bookCollected?"Book borrowed  ":"")+(State.componentCollected?"Component kit  ":"")+(State.printCollected?"Printout for "+catalog.recipient:"");
 if(inventory.Length>0)ReadableUI.Card(12,Screen.height-155,Mathf.Min(390,Screen.width*.4f),"INVENTORY\n"+inventory,ReadableUI.Small);
 }
 void Update(){
 if(!State.printing)return;var hud=DesktopHUD.Instance;var m=MissionManager.Instance;
 if((hud!=null&&hud.IsPaused)||(m!=null&&m.Finished))return;
 printSeconds-=Time.deltaTime;if(printSeconds<=0&&State.FinishPrint()){
 if(m!=null)m.guide.Say("Your printout is ready. Collect it from the Xerox output tray on Floor 4.");
 }
 }
 void Tell(string title,string message){if(DesktopHUD.Instance!=null)DesktopHUD.Instance.ShowInfo(title,message);}
 void Complete(string id){var m=MissionManager.Instance;if(m!=null)m.Signal(id);}
 public void Use(CampusServiceInteractable target){
 var tm=SceneTransitionManager.Instance;var m=MissionManager.Instance;
 if(catalog==null||tm==null||tm.Busy||tm.CurrentLevel!=target.level||m==null||!m.Started||m.Finished)return;
 if(target.facility!=null)m.Visit(target.facility);
 switch(target.action){
 case CampusServiceAction.ChooseBook:
 if(target.selection<0||target.selection>=catalog.books.Length)return;
 if(State.ChooseBook(target.selection))Tell("Book selected",catalog.books[target.selection]+"\nTake this selection to the library issue desk.\n"+catalog.disclaimer);
 else Tell("Library","Your book is already issued. Collect it at the pickup point if you have not done so.");break;
 case CampusServiceAction.IssueBook:
 Tell("Library issue desk",State.IssueBook()?"Practice issue recorded. Collect your book from the pickup point.":State.book<0?"Choose a book from the display first.":"Already issued. Use the book pickup point.");break;
 case CampusServiceAction.CollectBook:
 if(State.CollectBook()){Complete("library:borrowed");Tell("Book collected",catalog.books[State.book]+" is in your session inventory. Ask library staff about actual return dates and borrowing rules.");}
 else Tell("Book pickup",State.bookCollected?"You already collected your book.":"Select a book and visit the issue desk first.");break;
 case CampusServiceAction.ChooseComponent:
 if(target.selection<0||target.selection>=catalog.components.Length)return;
 if(State.ChooseComponent(target.selection))Tell("Component selected",catalog.components[target.selection]+"\nVisit the component issue desk to submit the practice request.\n"+catalog.disclaimer);
 else Tell("Components","Your request is already issued. Collect your component kit.");break;
 case CampusServiceAction.IssueComponent:
 Tell("Component issue desk",State.IssueComponent()?"Practice request recorded. Collect the component kit from pickup.":State.component<0?"Choose a kit from the labelled bins first.":"Request already issued. Use component pickup.");break;
 case CampusServiceAction.CollectComponent:
 if(State.CollectComponent()){Complete("components:borrowed");Tell("Component collected",catalog.components[State.component]+" added to your session inventory. Confirm real eligibility, stock and return procedures with staff.");}
 else Tell("Component pickup",State.componentCollected?"You already collected your kit.":"Choose a kit and submit its request at the issue desk first.");break;
 case CampusServiceAction.Print:
 if(State.StartPrint()){printSeconds=1.5f;Tell("Printing",catalog.document+" is printing. Close this panel, then collect it from the output tray. This is a simulation; no real printing or payment occurs.");}
 else Tell("Xerox",State.delivered?"Printout already delivered.":State.printCollected?"You are carrying the printout. Deliver it on Floor 5.":State.printing?"Printing in progress. Please wait.":"Your printout is ready at the output tray.");break;
 case CampusServiceAction.CollectPrint:
 if(State.CollectPrint()){Complete("print:collected");Tell("Printout collected","Carry "+catalog.document+" to "+catalog.recipient+" in the right-side faculty area on Floor 5 (right when exiting the rear lift). It stays in your inventory during floor travel.");}
 else Tell("Output tray",State.delivered?"The printout was already delivered.":State.printCollected?"You already have the printout.":State.printing?"The printer is still working.":"Use the Xerox control panel first.");break;
 case CampusServiceAction.Deliver:
 if(State.Deliver()){Complete("print:delivered");Tell("Submission received","Printout handed to "+catalog.recipient+". This is a simulated orientation handover.");}
 else Tell(catalog.recipient,State.delivered?"Thank you. This printout has already been received.":"Please collect the printout from the Floor 4 Xerox machine first.");break;
 }
 }
 }
}
