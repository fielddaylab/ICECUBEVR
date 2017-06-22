using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Main : MonoBehaviour
{
  public GameObject debug_cube_prefab;

  GameObject camera_house;
  GameObject main_camera;
  GameObject portal;
  GameObject portal_camera;

  GameObject[,] debug_cubes;

  int cur_layer_i;
  int next_layer_i;
  int n_layers;
  int[] layers;

  bool mouse_captured;
  float mouse_x;
  float mouse_y;

  void Start()
  {
    camera_house  = GameObject.Find("CameraHouse");
    main_camera   = GameObject.Find("Main Camera");
    portal        = GameObject.Find("Portal");
    portal_camera = GameObject.Find("Portal Camera");

    n_layers = 5;
    layers = new int[n_layers];
    for(int i = 0; i < n_layers; i++)
      layers[i] = LayerMask.NameToLayer("Set_"+i);
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
      cur_layer_i = next_layer_i;
      next_layer_i = (next_layer_i+1)%n_layers;

      main_camera.GetComponent<Camera>().cullingMask = 1 << layers[cur_layer_i];
      portal_camera.GetComponent<Camera>().cullingMask = 1 << layers[next_layer_i];
      portal.layer = layers[cur_layer_i];
    }

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

