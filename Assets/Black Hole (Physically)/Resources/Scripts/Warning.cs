using UnityEngine;
using System.Collections;

namespace BHP
{
    [ExecuteInEditMode]
    public class Warning : MonoBehaviour
    {
        void OnGUI()
        {
            GUI.Box(new Rect(50, 50, 190, 70), "ATTENTION");
            GUI.Label(new Rect(59, 70, 190, 90), "Please add your own space skybox material for getting a realistic result!");
        }
    }
}