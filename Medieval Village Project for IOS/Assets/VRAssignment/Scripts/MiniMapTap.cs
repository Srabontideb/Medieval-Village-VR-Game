using UnityEngine;
using UnityEngine.EventSystems;

namespace VRAssignment
{
    /// <summary>Forwards taps on the mini-map to MiniMap.TapAt.</summary>
    public class MiniMapTap : MonoBehaviour, IPointerClickHandler
    {
        public MiniMap map;
        public void OnPointerClick(PointerEventData e) => map.TapAt(e);
    }
}
