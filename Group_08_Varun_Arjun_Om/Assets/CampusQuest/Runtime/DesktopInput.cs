using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
namespace CampusQuest {
public static class DesktopInput {
 public static Vector2 Move {
 get {
#if ENABLE_INPUT_SYSTEM
 var k=Keyboard.current; if(k==null) return Vector2.zero;
 return Vector2.ClampMagnitude(new Vector2((k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0)),1);
#elif ENABLE_LEGACY_INPUT_MANAGER
 return Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"),Input.GetAxisRaw("Vertical")),1);
#else
 return Vector2.zero;
#endif
 }}
 public static Vector2 Look {get {
#if ENABLE_INPUT_SYSTEM
 return Mouse.current==null?Vector2.zero:Mouse.current.delta.ReadValue()*.09f;
#elif ENABLE_LEGACY_INPUT_MANAGER
 return new Vector2(Input.GetAxis("Mouse X"),Input.GetAxis("Mouse Y"))*2;
#else
 return Vector2.zero;
#endif
 }}
 public static bool Key(string key) {
#if ENABLE_INPUT_SYSTEM
 var k=Keyboard.current;if(k==null)return false;
 switch(key){case "E":return k.eKey.wasPressedThisFrame;case "Esc":return k.escapeKey.wasPressedThisFrame;case "F1":return k.f1Key.wasPressedThisFrame;default:return false;}
#elif ENABLE_LEGACY_INPUT_MANAGER
 return Input.GetKeyDown(key=="E"?KeyCode.E:key=="F1"?KeyCode.F1:KeyCode.Escape);
#else
 return false;
#endif
 }
}
}
