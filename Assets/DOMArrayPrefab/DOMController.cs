using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class DOMController : MonoBehaviour
{
  public bool on = false;
  public float lastCharge = 0.0f;
  public int stringNum = 0;
  public int domNum = 0;

  private GameObject eventSphere = null;
  private float oldScale = 1.0f;
  private Color defaultColor = new UnityEngine.Color(0.5f, 0.5f, 0.5f, 1.0f);

  void Start()
  {
    Transform t = transform.Find("low_poly_sphere");
    if(t) eventSphere = t.gameObject;
    else eventSphere = gameObject;

    oldScale = eventSphere.transform.localScale.x;
  }

  public void TurnOn(float fTimeFrac, float fRadius)
  {
    if(eventSphere != null)
    {
      eventSphere.transform.localScale = new Vector3(fRadius, fRadius, fRadius);
      float h = (fTimeFrac * 0.69f) - 0.02f;
      if(h < 0f) h += 1f;

      UnityEngine.Color c = UnityEngine.Color.HSVToRGB(h, 1f, 1f);
      eventSphere.GetComponent<MeshRenderer>().material.SetColor("_MKGlowColor", c);
      eventSphere.GetComponent<MeshRenderer>().material.SetColor("_MKGlowTexColor", c);
    }

    on = true;
  }

  public void TurnOff()
  {
    if(eventSphere)
    {
      eventSphere.transform.localScale = new Vector3(oldScale, oldScale, oldScale);
      eventSphere.GetComponent<MeshRenderer>().material.SetColor("_MKGlowColor", defaultColor);
      eventSphere.GetComponent<MeshRenderer>().material.SetColor("_MKGlowTexColor", defaultColor);
    }

    on = false;
  }
}

