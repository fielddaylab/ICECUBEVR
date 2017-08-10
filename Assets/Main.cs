using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Main : MonoBehaviour
{
  public GameObject debug_cube_prefab;

  GameObject camera_house;
  GameObject main_camera;
  Skybox main_camera_skybox;
  GameObject portal_lift;
  GameObject portal_projection;
  GameObject portal;
  GameObject portal_disk_prev;
  GameObject portal_disk_next;
  GameObject portal_border;
  GameObject portal_camera_next;
  GameObject portal_camera_prev;
  Skybox portal_camera_next_skybox;
  Skybox portal_camera_prev_skybox;
  GameObject helmet;
  GameObject satellite;
  GameObject earth;
  GameObject galaxy;
  GameObject black_hole;
  GameObject cam_reticle;
  GameObject cam_spinner;
  GameObject gaze_lift;
  GameObject gaze_projection;
  GameObject gaze;
  GameObject gaze_reticle;
  GameObject gaze_spinner;
  GameObject eyeray;
  GameObject ar_camera_project;
  GameObject ar_camera_static;
  GameObject ar_quad;

  Vector3 default_portal_scale;
  Vector3 default_portal_position;
  Vector3 default_look_ahead;
  Vector3 look_ahead;
  Vector3 lazy_look_ahead;
  Vector3 player_head;
  Vector3 default_satellite_position;
  Vector3 satellite_position;
  Vector3 satellite_velocity;

  public Material alpha_material;
  int alpha_id;
  float flash_alpha;

  public AudioClip sound;
  AudioSource track_source;
  AudioSource sfx_source;

  public GameObject star_prefab;

  GameObject[,] debug_cubes;
  string[] audio_files = new string[] {
   "tracks/0_intro_0",
   "tracks/0_intro_1",
   "tracks/0_intro_2",
   "tracks/1_helio_0",
   "tracks/1_helio_1",
   "tracks/2_milky_0",
   "tracks/2_milky_1",
   "tracks/3_black_0",
   "tracks/3_black_1",
   "tracks/3_black_2",
   "tracks/4_return_0",
   "tracks/5_back_0",
   "tracks/5_back_1",
   "tracks/6_kuiper_0",
   "tracks/6_kuiper_1",
   "tracks/7_local_0",
   "tracks/7_local_1",
   "tracks/8_pulsar_0",
   "tracks/8_pulsar_1",
   "tracks/8_pulsar_2",
   "tracks/9_back_0",
   "tracks/9_back_1",
   "tracks/9_back_2",
   "tracks/9_back_3",
   "tracks/10_helio_0",
   "tracks/11_grav_0",
   "tracks/11_grav_1",
   "tracks/12_nothing_0",
   "tracks/12_nothing_1",
   "tracks/13_return_0",
   "tracks/13_return_1",
   "tracks/14_alpha_0",
   "tracks/14_alpha_1",
   "tracks/14_alpha_2",
   "tracks/15_milky_0",
   "tracks/15_milky_1",
   "tracks/16_magnet_0",
   "tracks/16_magnet_1",
   "tracks/16_magnet_2",
   "tracks/16_magnet_3",
   "tracks/17_return_0",
   "tracks/18_debrief_0",
   "tracks/18_debrief_1"
  };
  AudioClip[] audio_clips;
  int cur_audio_playing_i;
  int cur_audio_playing_section;

  string[] skybox_files = new string[] {
   "GalaxyBox2/Skybox2_1/Skybox2_1",
   "GalaxyBox2/Skybox2_2/Skybox2_2",
   "GalaxyBox2/Skybox2_3/Skybox2_3",
   "GalaxyBox2/Skybox2_4/Skybox2_4",
   "GalaxyBox2/Skybox2_5/Skybox2_5",
   "GalaxyBox2/Skybox2_6/Skybox2_6",
   "GalaxyBox2/Skybox2_7/Skybox2_7",
   "GalaxyBox2/Skybox2_8/Skybox2_8",
   "GalaxyBox2/Skybox2_9/Skybox2_9",
   "GalaxyBox2/Skybox2_10/Skybox2_10"
  };
  Material[] skyboxes;
  int cur_skybox_i;

  int cur_layer_i;
  int next_layer_i;
  int prev_layer_i;
  int n_layers;
  int[] layers;
  int all_layer;
  int cam_layer;
  int portal_layer;
  int stars_layer;

  bool mouse_captured;
  bool mouse_just_captured;
  float mouse_x;
  float mouse_y;

  int in_portal_motion;
  int out_portal_motion;
  int max_portal_motion;

  int gaze_t_max;
  int gaze_t_since; //if positive, time since entered. if negative, time since exited.
  int gaze_t_in; //grows to max when in, shrinks to 0 when out
  int gaze_t_run; //grows while not fully out. 0 when fully out
  int gaze_t_numb; //countdown when distance should be ignored

  Vector3 gaze_pt;
  Vector2 gaze_cam_euler;

  Vector2 getEuler(Vector3 v)
  {
    float plane_dist = new Vector2(v.x,v.z).magnitude;
    return new Vector2(Mathf.Atan2(v.y,plane_dist),-1*(Mathf.Atan2(v.z,v.x)-Mathf.PI/2));
  }
  Vector2 getCamEuler(Vector3 v)
  {
    return getEuler(v-main_camera.transform.position);
  }
  Quaternion rotationFromEuler(Vector2 euler)
  {
    return Quaternion.Euler(-Mathf.Rad2Deg*euler.x, Mathf.Rad2Deg*euler.y, 0);
  }

  void Start()
  {
    camera_house       = GameObject.Find("CameraHouse");
    main_camera        = GameObject.Find("Main Camera");
    main_camera_skybox = main_camera.GetComponent<Skybox>();
    portal_lift        = GameObject.Find("Portal_Lift");
    portal_projection  = GameObject.Find("Portal_Projection");
    portal             = GameObject.Find("Portal");
    portal_disk_next   = GameObject.Find("Disk_Next");
    portal_disk_prev   = GameObject.Find("Disk_Prev");
    portal_border      = GameObject.Find("Border");
    portal_camera_next = GameObject.Find("Portal_Camera_Next");
    portal_camera_prev = GameObject.Find("Portal_Camera_Prev");
    portal_camera_prev_skybox = portal_camera_prev.GetComponent<Skybox>();
    portal_camera_next_skybox = portal_camera_next.GetComponent<Skybox>();
    helmet             = GameObject.Find("Helmet");
    satellite          = GameObject.Find("Satellite");
    earth              = GameObject.Find("Earth");
    galaxy             = GameObject.Find("Galaxy");
    black_hole         = GameObject.Find("Black_Hole");
    cam_reticle        = GameObject.Find("Cam_Reticle");
    cam_spinner        = GameObject.Find("Cam_Spinner");
    gaze_lift          = GameObject.Find("Gaze_Lift");
    gaze_projection    = GameObject.Find("Gaze_Projection");
    gaze               = GameObject.Find("Gaze");
    gaze_reticle       = GameObject.Find("Gaze_Reticle");
    gaze_spinner       = GameObject.Find("Gaze_Spinner");
    eyeray             = GameObject.Find("Ray");
    ar_camera_project  = GameObject.Find("AR_Camera_Project");
    ar_camera_static   = GameObject.Find("AR_Camera_Static");
    ar_quad            = GameObject.Find("AR_Quad");

    alpha_id = Shader.PropertyToID("alpha");
    flash_alpha = 0;
    track_source = GameObject.Find("Script").AddComponent<AudioSource>();
    sfx_source   = GameObject.Find("Script").AddComponent<AudioSource>();

    default_portal_scale = portal.transform.localScale;
    default_portal_position = portal.transform.position;

    default_satellite_position = new Vector3(1.5f,1.5f,10);
    satellite_position = default_satellite_position;
    satellite_velocity = new Vector3(0,0,-0.01f);
    satellite.transform.position = satellite_position;

    default_look_ahead = new Vector3(0,0,1);
    look_ahead = default_look_ahead;
    lazy_look_ahead = default_look_ahead;
    player_head = new Vector3(0,2,0);

    n_layers = 5;
    layers = new int[n_layers];
    for(int i = 0; i < n_layers; i++)
      layers[i] = LayerMask.NameToLayer("Set_"+i);
    all_layer = LayerMask.NameToLayer("Set_ALL");
    cam_layer = LayerMask.NameToLayer("Set_Cam_Only");
    portal_layer = LayerMask.NameToLayer("Set_Portal_Only");
    stars_layer = LayerMask.NameToLayer("Set_Stars");
    cur_layer_i = 0;
    helmet.layer = layers[cur_layer_i];
    prev_layer_i = 0;
    next_layer_i = 1;

/*
    debug_cubes = new GameObject[n_layers,3*3*3];
    for(int l = 0; l < n_layers; l++)
    for(int x = -1; x <= 1; x++)
    for(int y = -1; y <= 1; y++)
    for(int z = -1; z <= 1; z++)
    {
      int i = ((x+1)*9+(y+1)*3+(z+1)*1);
      debug_cubes[l,i] = (GameObject)Instantiate(debug_cube_prefab);
      if(x == y && y == z  && z == 0) debug_cubes[l,i].transform.position = new Vector3(Random.Range(-100.0f,100.0f),Random.Range(-100.0f,100.0f),Random.Range(-100.0f,100.0f));
      else                 debug_cubes[l,i].transform.position = new Vector3(x*10,y*10,z*10);
      debug_cubes[l,i].layer = layers[l];
      switch(l)
      {
        case 0: debug_cubes[l,i].GetComponent<Renderer>().material.SetColor("_Color", Color.white); break;
        case 1: debug_cubes[l,i].GetComponent<Renderer>().material.SetColor("_Color", Color.red); break;
        case 2: debug_cubes[l,i].GetComponent<Renderer>().material.SetColor("_Color", Color.yellow); break;
        case 3: debug_cubes[l,i].GetComponent<Renderer>().material.SetColor("_Color", Color.green); break;
        case 4: debug_cubes[l,i].GetComponent<Renderer>().material.SetColor("_Color", Color.blue); break;
      }
    }
*/

    mouse_captured = false;
    mouse_just_captured = true;
    mouse_x = Screen.width/2;
    mouse_y = Screen.height/2;

    camera_house.transform.rotation = Quaternion.Euler((mouse_y-Screen.height/2)*-2, (mouse_x-Screen.width/2)*2, 0);

    in_portal_motion = 0;
    out_portal_motion = 0;
    max_portal_motion = 100;

    gaze_t_max = 200;
    gaze_t_since = 0;
    gaze_t_in = 0;
    gaze_t_run = 0;
    gaze_t_numb = 0;

    GameObject[] star_groups;
    GameObject star;
    Vector3[] star_positions;
    Vector3 starpos;

    int n_stars = 0;//4000;
    int n_groups = (int)Mathf.Ceil(n_stars/1000);
    int n_stars_in_group;
    star_groups = new GameObject[n_groups];
    star = (GameObject)Instantiate(star_prefab);

    //gen positions
    star_positions = new Vector3[n_stars];
    for(int i = 0; i < n_stars; i++)
    {
      bool good_star = false;
      starpos = new Vector3(Random.Range(-1f,1f),Random.Range(-1f,1f),Random.Range(-1f,1f));
      good_star = (starpos.sqrMagnitude < Random.Range(0f,1f));
      while(!good_star)
      {
        starpos = new Vector3(Random.Range(-1f,1f),Random.Range(-1f,1f),Random.Range(-1f,1f));
        good_star = (starpos.sqrMagnitude < Random.Range(0f,1f));
      }
      starpos = starpos.normalized;
      star_positions[i] = starpos;
    }

    //morph positions
    for(int n = 0; n < 2; n++)
    {
      for(int i = 0; i < n_stars; i++)
      {
        Vector3 delta = new Vector3(0,0,0);
        Vector3 dist;
        for(int j = 0; j < n_stars; j++)
        {
          if(j != i)
          {
            dist = star_positions[j]-star_positions[i];
            if(dist.sqrMagnitude < 0.1)
            {
              delta += dist*(0.0001f/dist.sqrMagnitude);
            }
          }
        }
        star_positions[i] = (star_positions[i]+delta).normalized;
      }
    }

    //gen assets
    for(int i = 0; i < n_groups; i++)
    {
      n_stars_in_group = Mathf.Min(1000,n_stars);
      CombineInstance[] combine = new CombineInstance[n_stars_in_group];

      for(int j = 0; j < n_stars_in_group; j++)
      {
        starpos = star_positions[i*n_stars_in_group+j];
        float r = Random.Range(0f,1f);
        starpos *= Mathf.Lerp(20f,30f,r*r);

        star.transform.position = starpos;
        star.transform.rotation = Quaternion.Euler(Random.Range(0f,360f),Random.Range(0f,360f),Random.Range(0f,360f));
        star.transform.localScale = new Vector3(0.05f,0.05f,0.05f);

        combine[j].mesh = star.transform.GetComponent<MeshFilter>().mesh;
        combine[j].transform = star.transform.localToWorldMatrix;
      }

      star_groups[i] = (GameObject)Instantiate(star_prefab);
      star_groups[i].layer = stars_layer;
      star_groups[i].transform.position = new Vector3(0,0,0);
      star_groups[i].transform.rotation = Quaternion.Euler(0,0,0);
      star_groups[i].transform.localScale = new Vector3(1,1,1);
      star_groups[i].transform.GetComponent<MeshFilter>().mesh = new Mesh();
      star_groups[i].transform.GetComponent<MeshFilter>().mesh.CombineMeshes(combine);

      n_stars -= n_stars_in_group;
    }
    Destroy(star,0f);

    gaze_pt = player_head;
    while(Vector3.Distance(gaze_pt,player_head) < 1.5)
    {
      gaze_pt = new Vector3(
        Random.Range(-1.0f,1.0f),
        Random.Range(0.2f,0.8f),
        Random.Range(-1.0f,1.0f)
      ).normalized;
    }
    gaze_pt = gaze_pt * 100;
    gaze_cam_euler = getCamEuler(gaze_pt);

    eyeray.GetComponent<LineRenderer>().SetPosition(0,(gaze_pt*-100)+new Vector3(10,0,0));
    eyeray.GetComponent<LineRenderer>().SetPosition(1,gaze_pt*100);

    gaze_projection.transform.rotation = rotationFromEuler(gaze_cam_euler);
    portal_projection.transform.rotation = rotationFromEuler(gaze_cam_euler);

    earth.transform.position = gaze_pt*-2;
    galaxy.transform.position = gaze_pt*-2;
    black_hole.transform.position = gaze_pt*10;

    audio_clips = new AudioClip[audio_files.Length];
    for(int i = 0; i < audio_files.Length; i++)
      audio_clips[i] = Resources.Load<AudioClip>(audio_files[i]);
    cur_audio_playing_i = -1;
    cur_audio_playing_section = 0;

    skyboxes = new Material[skybox_files.Length];
    for(int i = 0; i < skybox_files.Length; i++)
      skyboxes[i] = Resources.Load<Material>(skybox_files[i]);
    cur_skybox_i = 0;
    main_camera_skybox.material = skyboxes[cur_skybox_i];
    portal_camera_prev_skybox.material = skyboxes[cur_skybox_i];
    portal_camera_next_skybox.material = skyboxes[cur_skybox_i+1];
  }

  void Update()
  {
    ar_camera_project.GetComponent<Camera>().aspect = main_camera.GetComponent<Camera>().aspect;
    ar_camera_static.GetComponent<Camera>().aspect = main_camera.GetComponent<Camera>().aspect;
    if(Input.GetMouseButtonDown(0))
    {
      mouse_captured = !mouse_captured;
      if(mouse_captured)
      {
        mouse_just_captured = true;
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
    }

    if(in_portal_motion > 0) in_portal_motion++;
    if(in_portal_motion > max_portal_motion)
    {
      in_portal_motion = 0;
      out_portal_motion = 1;
      prev_layer_i = cur_layer_i;
      cur_layer_i = next_layer_i;
      next_layer_i = (next_layer_i+1)%n_layers;

      if(cur_layer_i == 3)  main_camera.GetComponent<Camera>().cullingMask        = (1 << layers[cur_layer_i])  | (1 << cam_layer)    | (1 << all_layer);
      else                  main_camera.GetComponent<Camera>().cullingMask        = (1 << layers[cur_layer_i])  | (1 << cam_layer)    | (1 << all_layer) | (1 << stars_layer);
      if(next_layer_i == 3) portal_camera_next.GetComponent<Camera>().cullingMask = (1 << layers[next_layer_i]) | (1 << portal_layer) | (1 << all_layer);
      else                  portal_camera_next.GetComponent<Camera>().cullingMask = (1 << layers[next_layer_i]) | (1 << portal_layer) | (1 << all_layer) | (1 << stars_layer);
      if(prev_layer_i == 3) portal_camera_prev.GetComponent<Camera>().cullingMask = (1 << layers[prev_layer_i]) | (1 << portal_layer) | (1 << all_layer);
      else                  portal_camera_prev.GetComponent<Camera>().cullingMask = (1 << layers[prev_layer_i]) | (1 << portal_layer) | (1 << all_layer) | (1 << stars_layer);
      portal_lift.layer = layers[cur_layer_i];
      portal_projection.layer = layers[cur_layer_i];
      portal.layer = layers[cur_layer_i];
      portal_disk_next.layer = layers[cur_layer_i];
      portal_disk_prev.layer = layers[cur_layer_i];
      helmet.layer = layers[cur_layer_i];
      foreach(Transform child in helmet.transform)
         child.gameObject.layer = layers[cur_layer_i];
      ar_quad.layer = layers[cur_layer_i];

      cur_skybox_i = (cur_skybox_i+1)%skybox_files.Length;
      main_camera_skybox.material = skyboxes[cur_skybox_i];
      portal_camera_prev_skybox.material = skyboxes[cur_skybox_i];
      portal_camera_next_skybox.material = skyboxes[(cur_skybox_i+1)%skybox_files.Length];
    }
    if(out_portal_motion > 0) out_portal_motion++;
    if(out_portal_motion > max_portal_motion)
    {
      out_portal_motion = 0;
    }

    if(in_portal_motion > 0)
    {
      float t = in_portal_motion/(float)max_portal_motion;
      portal.transform.localPosition = new Vector3(default_portal_position.x,default_portal_position.y,Mathf.Lerp(default_portal_position.z,0,t));
      float engulf = t-1;
      engulf *= -engulf;
      engulf += 1;
      portal.transform.localScale = new Vector3(default_portal_scale.x*engulf,default_portal_scale.y*engulf,default_portal_scale.z*engulf);
    }
    else if(out_portal_motion > 0)
    {
      float t = out_portal_motion/(float)max_portal_motion;
      portal.transform.localPosition = new Vector3(default_portal_position.x,default_portal_position.y,Mathf.Lerp(0,-default_portal_position.z,t));
      float engulf = t;
      engulf *= -engulf;
      engulf += 1;
      portal.transform.localScale = new Vector3(default_portal_scale.x*engulf,default_portal_scale.y*engulf,default_portal_scale.z*engulf);
    }
    else
    {
      portal.transform.localPosition = default_portal_position;
      portal.transform.localScale = new Vector3(0,0,0);
    }

    if(mouse_captured)
    {
      float in_x = Input.GetAxis("Mouse X")*10;
      float in_y = Input.GetAxis("Mouse Y")*10;
      if(!mouse_just_captured)
      {
        mouse_x += in_x;
        mouse_y += in_y;
        camera_house.transform.rotation = Quaternion.Euler((mouse_y-Screen.height/2)*-2, (mouse_x-Screen.width/2)*2, 0);
      }
      else
      {
        if(in_x != 0 || in_y != 0) mouse_just_captured = false;
      }
    }

    Vector3 offset = new Vector3(
      -main_camera.transform.localPosition.x,
      -main_camera.transform.localPosition.y, 
      -main_camera.transform.localPosition.z);
    camera_house.transform.localPosition = offset+player_head;

    look_ahead = main_camera.transform.rotation*default_look_ahead;
    lazy_look_ahead = Vector3.Lerp(lazy_look_ahead,look_ahead,0.1f);
    helmet.transform.position = main_camera.transform.position;
    helmet.transform.rotation = rotationFromEuler(getEuler(lazy_look_ahead));

    satellite_position += satellite_velocity;
    if(cur_layer_i != 1) satellite_position = default_satellite_position;
    satellite.transform.position = satellite_position;

    if(in_portal_motion > 0)
      flash_alpha = (float)in_portal_motion/max_portal_motion;
    else if(out_portal_motion > 0)
      flash_alpha = 1.0f-((float)out_portal_motion/max_portal_motion);
    else
      flash_alpha = 0;
    flash_alpha *= 1.1f;
    if(flash_alpha > 1) flash_alpha = 1;
    flash_alpha = flash_alpha*flash_alpha;
    flash_alpha = flash_alpha*flash_alpha;
    alpha_material.SetFloat(alpha_id,flash_alpha);

    float shrink;
    float rot;

    shrink = (float)gaze_t_in/(float)gaze_t_max;
    rot = ((float)gaze_t_run/gaze_t_max)*5*360.0f;

    gaze_spinner.transform.localScale = new Vector3(shrink,shrink,shrink);
    gaze_spinner.transform.localRotation = Quaternion.Euler(0,0,rot);
    cam_spinner.transform.localScale = new Vector3(shrink,shrink,shrink);
    cam_spinner.transform.localRotation = Quaternion.Euler(0,0,rot);

    float distance = Vector3.Distance(gaze_reticle.transform.position,cam_reticle.transform.position);
    if(gaze_t_numb == 0 && distance < 0.2)
    {
      if(gaze_t_since < 0) gaze_t_since = 1;
      else                 gaze_t_since++;
      if(gaze_t_in < gaze_t_max) gaze_t_in++;
      if(gaze_t_in == gaze_t_max) gaze_t_numb = gaze_t_max*2;

      //advance
      if(gaze_t_in == gaze_t_max)
      {
        if(in_portal_motion == 0 && out_portal_motion == 0) in_portal_motion = 1;
        if(track_source.isPlaying) track_source.Stop();

        cur_audio_playing_section++;
        while(!track_source.isPlaying)
        {
          string next_file = audio_files[(cur_audio_playing_i+1)%audio_files.Length];
          int u_i = next_file.IndexOf("_");
          string number = next_file.Substring(7,u_i-7);
          int section = int.Parse(number);
          cur_audio_playing_i = (cur_audio_playing_i+1)%audio_files.Length;
          if(section == cur_audio_playing_section)
          {
            track_source.clip = audio_clips[cur_audio_playing_i];
            track_source.Play();
          }
        }
      }
    }
    else
    {
      if(gaze_t_since > 0) gaze_t_since = -1;
      else                 gaze_t_since--;
      if(gaze_t_in > 0) gaze_t_in--;
    }
    if(gaze_t_in > 0) gaze_t_run++;
    else              gaze_t_run = 0;
    if(gaze_t_numb > 0) gaze_t_numb--;

    if(!track_source.isPlaying)
    {
      string next_file = audio_files[(cur_audio_playing_i+1)%audio_files.Length];
      int u_i = next_file.IndexOf("_");
      string number = next_file.Substring(7,u_i-7);
      int section = int.Parse(number);
      if(section == cur_audio_playing_section)
      {
        cur_audio_playing_i = (cur_audio_playing_i+1)%audio_files.Length;
        track_source.clip = audio_clips[cur_audio_playing_i];
        track_source.volume = 1;
        track_source.Play();
      }
    }
  }

}

