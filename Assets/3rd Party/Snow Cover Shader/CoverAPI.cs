using UnityEngine;
using System.Collections;

public class CoverAPI : MonoBehaviour {

    public float coverTime = 5f;
    public bool autoStart = false;

    public bool startCover = false;
    float currentCover = 0f;
    float t = 0f;

    public void SetColor(Color color)
    {
        GetComponent<Renderer>().material.SetColor("_SnowColor", color);
    }

    public void SetSnow(float value)
    {
        GetComponent<Renderer>().material.SetFloat("_SnowAmount", value);
    }
	
    void Start()
    {
        if (autoStart)
            startCover = true;
    }

	// Update is called once per frame
	void Update () {
        if (!startCover)
            return;

        currentCover = Mathf.Lerp(0, 20, t);
        if (t <= 1)
        {
            t += Time.deltaTime / coverTime;
            SetSnow(currentCover);
        }
        else
            startCover = false;
    }
}
