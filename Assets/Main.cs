using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Main : MonoBehaviour
{
  public GameObject debug_cube_prefab;

  GameObject camera_house;
  GameObject main_camera;
  GameObject portal;
  GameObject portal_disk;
  GameObject portal_border;
  GameObject portal_camera;
  Vector3 portal_scale;
  Vector3 portal_translate;
  Material portal_material;

  GameObject[,] debug_cubes;

  int cur_layer_i;
  int next_layer_i;
  int n_layers;
  int[] layers;
  int all_layer;

  bool mouse_captured;
  float mouse_x;
  float mouse_y;

  int portal_motion;

  void Start()
  {
    camera_house     = GameObject.Find("CameraHouse");
    main_camera      = GameObject.Find("Main Camera");
    portal           = GameObject.Find("Portal");
    portal_disk      = GameObject.Find("Disk");
    portal_border    = GameObject.Find("Border");
    portal_camera    = GameObject.Find("Portal Camera");
    portal_material  = portal_disk.GetComponent<Renderer>().material;
    portal_scale = portal.transform.localScale;
    portal_translate = portal.transform.position;

    n_layers = 5;
    layers = new int[n_layers];
    for(int i = 0; i < n_layers; i++)
      layers[i] = LayerMask.NameToLayer("Set_"+i);
    all_layer = LayerMask.NameToLayer("Set_ALL");
    cur_layer_i = 0;
    next_layer_i = 1;

    debug_cubes = new GameObject[n_layers,3*3*3];
    for(int l = 0; l < n_layers; l++)
    for(int x = -1; x <= 1; x++)
    for(int y = -1; y <= 1; y++)
    for(int z = -1; z <= 1; z++)
    {
      int i = ((x+1)*9+(y+1)*3+(z+1)*1);
      debug_cubes[l,i] = (GameObject)Instantiate(debug_cube_prefab);
      debug_cubes[l,i].transform.position = new Vector3(x*10,y*10,z*10);
      debug_cubes[l,i].layer = layers[l];
      switch(l)
      {
        case 0: debug_cubes[l,i].GetComponent<Renderer>().material.SetColor("_Color", Color.red); break;
        case 1: debug_cubes[l,i].GetComponent<Renderer>().material.SetColor("_Color", Color.white); break;
        case 2: debug_cubes[l,i].GetComponent<Renderer>().material.SetColor("_Color", Color.yellow); break;
        case 3: debug_cubes[l,i].GetComponent<Renderer>().material.SetColor("_Color", Color.green); break;
        case 4: debug_cubes[l,i].GetComponent<Renderer>().material.SetColor("_Color", Color.blue); break;
      }
    }

    mouse_captured = false;
    mouse_x = 0;
    mouse_y = 0;

    portal_motion = 0;
  }

  void Update()
  {
    if(Input.GetMouseButtonDown(0))
    {
      mouse_captured = !mouse_captured;
      if(mouse_captured)
      {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
      }
      else
      {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
      }
    }

    if(Input.GetKeyDown("space"))
    {
      if(portal_motion == 0) portal_motion = 1;
    }

    if(portal_motion > 0) portal_motion++;
    if(portal_motion > 100)
    {
      portal_motion = 0;
      cur_layer_i = next_layer_i;
      next_layer_i = (next_layer_i+1)%n_layers;

      main_camera.GetComponent<Camera>().cullingMask = (1 << layers[cur_layer_i]) | (1 << all_layer);
      portal_camera.GetComponent<Camera>().cullingMask = (1 << layers[next_layer_i]) | (1 << all_layer);
      portal.layer = layers[cur_layer_i];
      portal_disk.layer = layers[cur_layer_i];
      portal_border.layer = layers[cur_layer_i];
    }

    float t = portal_motion/100.0f;
    portal.transform.localPosition = new Vector3(portal_translate.x,portal_translate.y,Mathf.Lerp(10,1,t));
    portal.transform.localScale = new Vector3(portal_scale.x*(1+t),portal_scale.y*(1+t),portal_scale.z*(1+t));

    if(mouse_captured)
    {
      mouse_x += Input.GetAxis("Mouse X")*10;
      mouse_y += Input.GetAxis("Mouse Y")*10;
      camera_house.transform.rotation = Quaternion.Euler((mouse_y-Screen.height/2)*-2, (mouse_x-Screen.width/2)*2, 0);
    }
    else
    {
      mouse_x = 0;
      mouse_y = 0;
    }
  }

}

