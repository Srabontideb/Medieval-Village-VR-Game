using UnityEngine;
using UnityEngine.EventSystems;

namespace VRAssignment
{
    /// <summary>An on-screen button that reports whether it is being held down (for the move pad).</summary>
    public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public bool Held { get; private set; }

        public void OnPointerDown(PointerEventData e) => Held = true;
        public void OnPointerUp(PointerEventData e) => Held = false;
        public void OnPointerExit(PointerEventData e) => Held = false;
        void OnDisable() => Held = false;
    }
}
