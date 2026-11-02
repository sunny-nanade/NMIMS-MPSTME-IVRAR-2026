using UnityEngine;
namespace CampusQuest {
public class DesktopHUD:MonoBehaviour {
 public static DesktopHUD Instance {get;private set;}
 public DesktopPlayerController player;
 public bool enableDebug=true;
 [Range(18,36)]public int textSize=24;
 const string TextPreference="CampusQuest.Desktop.TextSize";
 MenuBoard menuBoard;bool pause,debug;string infoTitle,info;LiftController lift;Vector2 scroll;
 public bool IsPaused=>pause;
 public bool DebugMenuOpen=>debug;
 public bool HasInfo=>info!=null;
 public string InfoTitle=>infoTitle;
 public string InfoText=>info;
 public LiftController OpenLift=>lift;
 public MenuBoard OpenMenuBoard=>menuBoard;
 public bool LocalMenuOpen=>pause||debug||info!=null||lift!=null||menuBoard!=null;
 public bool MenuOpen=>LocalMenuOpen||(MissionUI.Instance!=null&&MissionUI.Instance.Blocking);
 void Awake(){Instance=this;textSize=Mathf.Clamp(PlayerPrefs.GetInt(TextPreference,textSize),18,36);if(player!=null&&player.useXRInput&&GetComponent<XRPresentationUI>()==null)gameObject.AddComponent<XRPresentationUI>();}
 public void SetTextSize(int value){textSize=Mathf.Clamp(value,18,36);PlayerPrefs.SetInt(TextPreference,textSize);PlayerPrefs.Save();}
 void Update(){
 if(DesktopInput.Key("Esc")){if(MenuOpen){CloseMenus();if(MissionUI.Instance!=null)MissionUI.Instance.DismissList();}else pause=true;}
 if(enableDebug&&(MissionUI.Instance==null||!MissionUI.Instance.Blocking)&&DesktopInput.Key("F1")){bool next=!debug;CloseMenus();debug=next;}
 Cursor.lockState=MenuOpen?CursorLockMode.None:CursorLockMode.Locked;Cursor.visible=MenuOpen;
 }
 public void ShowMenuBoard(MenuBoard value){CloseMenus();menuBoard=value;}
 public void ShowInfo(string title,string text){CloseMenus();infoTitle=title;info=text;}
 public void ShowLift(LiftController value){CloseMenus();lift=value;}
 public void OpenPauseMenu(){CloseMenus();pause=true;}
 public void CloseMenus(){pause=false;debug=false;info=null;lift=null;menuBoard=null;scroll=Vector2.zero;}
 void OnGUI(){
 if(XRPresentationUI.IsActive)return;
 var tm=SceneTransitionManager.Instance;var order=CanteenOrderManager.Instance;
 if(!MenuOpen){
 float width=Mathf.Min(520,(Screen.width-36)*.46f);
 ReadableUI.Card(12,12,width,(tm==null?"Loading campus":tm.Status)+"\nWASD move · Mouse look\nE interact · Esc menu",ReadableUI.Small);
 GUI.Label(new Rect(Screen.width/2-10,Screen.height/2-20,30,40),"+",ReadableUI.Title);
 if(player!=null&&player.Target!=null){string prompt="E — "+player.Target.prompt;float w=Mathf.Min(800,Screen.width-24);float h=ReadableUI.Body.CalcHeight(new GUIContent(prompt),w-28)+24;ReadableUI.Card((Screen.width-w)/2,Screen.height-h-12,w,prompt,ReadableUI.Body);}
 if(order!=null&&tm!=null&&tm.CurrentLevel==-1){float header=ReadableUI.Small.CalcHeight(new GUIContent(tm.Status+"\nWASD move · Mouse look\nE interact · Esc menu"),width-28)+24;ReadableUI.Card(12,24+header,width,"CANTEEN — "+order.Step+"\n"+(Screen.height>=600?order.Objective+"\n":"")+order.Feedback,ReadableUI.Small);}
 }
 if(LocalMenuOpen){
 var rect=ReadableUI.Modal;ReadableUI.Backdrop(rect);GUILayout.BeginArea(rect,ReadableUI.Panel);
 ReadableUI.SizeControls();scroll=GUILayout.BeginScrollView(scroll);
 if(menuBoard!=null&&order!=null){
 GUILayout.Label("Canteen menu",ReadableUI.Title);
 foreach(var item in order.menuItems)if(item!=null&&!item.isPlaceholder&&item.isVerified)if(ReadableUI.Button(item.displayName+" — INR "+item.priceINR,rect.width)){var board=menuBoard;board.Select(item);break;}
 GUILayout.Label("Choose one dish. No payment is taken.",ReadableUI.Body);
 }
 if(lift!=null){
 GUILayout.Label("Lift "+lift.cabinID+" — choose floor",ReadableUI.Title);
 var activeLift=lift;
 for(int row=0;row<2;row++){GUILayout.BeginHorizontal();for(int col=0;col<5;col++){int level=row*5+col-1;if(GUILayout.Button(FloorConfig.Label(level),ReadableUI.ButtonStyle,GUILayout.Height(Mathf.Max(60,textSize*2))))activeLift.Select(level);}GUILayout.EndHorizontal();}
 }
 if(info!=null){GUILayout.Label(infoTitle,ReadableUI.Title);GUILayout.Space(10);GUILayout.Label(info,ReadableUI.Body);}
 if(debug){
 GUILayout.Label("Development tools",ReadableUI.Title);
 if(MissionManager.Instance!=null&&ReadableUI.Button("Skip current mission — practice only",rect.width)){MissionManager.Instance.DebugSkip();CloseMenus();}
 if(order!=null&&ReadableUI.Button("Restart canteen practice — keep points",rect.width)){order.Replay();CloseMenus();}
 for(int i=-1;i<=8;i++){int target=i;if(ReadableUI.Button("Jump to floor "+FloorConfig.Label(i),rect.width)){if(MissionManager.Instance!=null)MissionManager.Instance.MarkDebug();CloseMenus();tm.Travel(target,"start");}}
 }
 if(pause){
 GUILayout.Label("Paused",ReadableUI.Title);
 if(MissionUI.Instance!=null&&ReadableUI.Button("Mission list and current hint",rect.width)){CloseMenus();MissionUI.Instance.ShowList=true;}
 if(ReadableUI.Button("Restart at ground entrance",rect.width)){if(MissionManager.Instance!=null)MissionManager.Instance.Restart();else{if(order!=null)order.ResetSession();CloseMenus();tm.Travel(0,"start");}}
 if(ReadableUI.Button("Quit",rect.width))Application.Quit();
 }
 if(ReadableUI.Button("Close / Resume",rect.width))CloseMenus();
 GUILayout.EndScrollView();GUILayout.EndArea();
 }
 if(tm!=null&&tm.Fade>0){var c=GUI.color;GUI.color=new Color(0,0,0,tm.Fade);GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture);GUI.color=c;}
 }
 void OnDestroy(){if(Instance==this)Instance=null;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
}
}

