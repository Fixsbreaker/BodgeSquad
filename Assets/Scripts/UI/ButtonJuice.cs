using UnityEngine;
using UnityEngine.EventSystems;

namespace BodgeSquad.UI
{
    // Chunky menu buttons: grow and tilt a little when hovered/selected, squash when pressed.
    public class ButtonJuice : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler,
                               IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] float hoverScale = 1.07f;
        [SerializeField] float hoverTilt = -1.5f;

        bool _hot, _down;
        float _scale = 1f, _vel, _tilt;

        public void OnPointerEnter(PointerEventData e) => _hot = true;
        public void OnPointerExit(PointerEventData e) { _hot = false; _down = false; }
        public void OnSelect(BaseEventData e) => _hot = true;
        public void OnDeselect(BaseEventData e) => _hot = false;
        public void OnPointerDown(PointerEventData e) => _down = true;
        public void OnPointerUp(PointerEventData e) => _down = false;

        void Update()
        {
            float target = _down ? 0.94f : _hot ? hoverScale : 1f;
            // springy: overshoots a touch, settles fast
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            _vel += (target - _scale) * 260f * dt;
            _vel *= Mathf.Exp(-18f * dt);
            _scale += _vel * dt;
            _tilt = Mathf.Lerp(_tilt, _hot ? hoverTilt : 0f, dt * 14f);
            transform.localScale = Vector3.one * _scale;
            transform.localRotation = Quaternion.Euler(0, 0, _tilt);
        }
    }
}
