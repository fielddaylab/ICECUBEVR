using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Main : MonoBehaviour
{
  GameObject camera_house;
  GameObject main_camera;
  Skybox main_camera_skybox;
  GameObject portal_projection;
  GameObject portal;
  GameObject portal_camera_next;
  Skybox portal_camera_next_skybox;
  GameObject helmet;
  GameObject helmet_light;
  Light helmet_light_light;
  GameObject cam_reticle;
  GameObject cam_spinner;
  GameObject gaze_projection;
  GameObject gaze_reticle;
  GameObject spec_projection;
  GameObject spec_viz_reticle;
  GameObject spec_gam_reticle;
  GameObject spec_neu_reticle;
  GameObject spec_sel_reticle;
  GameObject eyeray;
  GameObject ar_camera_project;
  GameObject ar_camera_static;
  GameObject ar_circle;
  GameObject ar_label;
  GameObject ar_label_offset;
  TextMesh ar_label_text;
  GameObject ar_alert;
  GameObject ar_timer;
  TextMesh ar_timer_text;

  GameObject[] icecube;
  GameObject[] voyager;
  GameObject[] milky;
  GameObject[] blackhole;
  GameObject[] earth;
  GameObject stars;
  GameObject starsscale;

  Vector3 default_portal_scale;
  Vector3 default_portal_position;
  Vector3 default_look_ahead;
  Vector3 look_ahead;
  Vector3 lazy_look_ahead;
  Vector3 very_lazy_look_ahead;
  Vector3 player_head;
  Vector3 default_satellite_position;
  Vector3 satellite_position;
  Vector3 satellite_velocity;

  public Material alpha_material;
  public GameObject star_prefab;

  public Color scene0_helmet_color;
  public Color scene1_helmet_color;
  public Color scene2_helmet_color;
  public Color scene3_helmet_color;
  public Color scene4_helmet_color;

  public int alpha_id;
  float flash_alpha;

  float alert_t;
  float timer_t;

  AudioSource track_source;
  AudioSource sfx_source;

  string[] audio_files = new string[] {
   "tracks/0_silence",
   "tracks/0_intro_0",
   "tracks/0_intro_1",
   "tracks/0_silence",
   "tracks/0_intro_2",
   "tracks/0_intro_3",
   "tracks/1_silence",
   "tracks/1_voyager_0",
   "tracks/1_voyager_1",
   "tracks/2_silence",
   "tracks/2_kaiper_0",
   "tracks/2_kaiper_1",
   "tracks/3_silence",
   "tracks/3_center_0",
   "tracks/3_center_1",
   "tracks/4_silence",
   "tracks/4_earth_00",
   "tracks/5_silence",
   "tracks/5_intro_0",
   "tracks/5_intro_1",
   "tracks/5_intro_2",
   "tracks/5_intro_3",
   "tracks/6_silence",
   "tracks/6_voyager_0",
   "tracks/6_voyager_1",
   "tracks/7_silence",
   "tracks/7_milky_0",
   "tracks/7_milky_1",
   "tracks/8_silence",
   "tracks/8_hole_0",
   "tracks/8_hole_1",
   "tracks/9_silence",
   "tracks/9_earth_0",
   "tracks/10_silence",
   "tracks/10_saggitarius_0",
   "tracks/10_saggitarius_1",
   "tracks/10_saggitarius_2",
   "tracks/10_saggitarius_3",
  };
  AudioClip[] audio_clips;
  int cur_audio_playing_i;
  int cur_audio_playing_section;

  string[] skybox_file_names = new string[] {
   "Classic Skybox/sky12", //ice
   "GalaxyBox2/Skybox2_1/Skybox2_1", //voyager
   "GalaxyBox2/Skybox2_4/Skybox2_4", //nothing
   "GalaxyBox2/Skybox2_1/Skybox2_1", //extreme
   "GalaxyBox2/Skybox2_1/Skybox2_1", //earth
  };
  string[,] skybox_files;
  Material[,] skyboxes;

  Vector3[] scene_centers = new Vector3[] {
    new Vector3(0f,0f,0f), //ice
    new Vector3(0f,0f,0f), //voyager
    new Vector3(0f,0f,0f), //nothing
    new Vector3(0f,0f,0f), //extreme
    new Vector3(0f,0f,0f), //earth
  };
  float[] scene_rots = new float[] {
    0f, //ice
    0f, //voyager
    0f, //nothing
    0f, //extreme
    0f, //earth
  };
  float[] scene_rot_deltas = new float[] {
    0.0f, //ice
    0.0f, //voyager
    0.0001f, //nothing
    0.0001f, //extreme
    0.00005f, //earth
  };

  enum SPEC { VIZ, GAM, NEU, COUNT };
  enum SCENE { ICE, VOYAGER, NOTHING, EXTREME, EARTH, COUNT };

  int cur_scene_i;
  int next_scene_i;
  int cur_spec_i;
  int[,] layers;
  string[] spec_names;
  string[] scene_names;
  string[,] layer_names;
  GameObject[,] scene_groups;
  int default_layer;

  bool mouse_captured;
  bool mouse_just_captured;
  float mouse_x;
  float mouse_y;

  float in_portal_motion;
  float out_portal_motion;
  float max_portal_motion;

  float gaze_t_max;
  float gaze_t_since; //if positive, time since entered. if negative, time since exited.
  float gaze_t_in; //grows to max when in, shrinks to 0 when out
  float gaze_t_run; //grows while not fully out. 0 when fully out
  float gaze_t_numb; //countdown when distance should be ignored

  float spec_t_max; //The time required to select a button 
  float spec_t_since; //if positive, time since entered. if negative, time since exited.
  float spec_t_in; //grows to max when in, shrinks to 0 when out
  float spec_t_run; //grows while not fully out. 0 when fully out
  float spec_t_numb; //countdown when distance should be ignored

  Vector3 gaze_pt;
  Vector3 anti_gaze_pt;
  Vector2 cam_euler;
  Vector2 gaze_cam_euler;
  Vector2 spec_euler;

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
    spec_names = new string[(int)SPEC.COUNT];
    for(int i = 0; i < (int)SPEC.COUNT; i++)
    {
      string name = "";
      switch(i)
      {
        case (int)SPEC.VIZ: name = "Viz"; break;
        case (int)SPEC.GAM: name = "Gam"; break;
        case (int)SPEC.NEU: name = "Neu"; break;
      }
      spec_names[i] = name;
    }

    scene_names = new string[(int)SCENE.COUNT];
    for(int i = 0; i < (int)SCENE.COUNT; i++)
    {
      string name = "";
      switch(i)
      {
        case (int)SCENE.ICE:     name = "Ice"; break;
        case (int)SCENE.VOYAGER: name = "Voyager"; break;
        case (int)SCENE.NOTHING: name = "Nothing"; break;
        case (int)SCENE.EXTREME: name = "Extreme"; break;
        case (int)SCENE.EARTH:   name = "Earth"; break;
      }
      scene_names[i] = name;
    }

    layer_names = new string[(int)SPEC.COUNT,(int)SCENE.COUNT];
    for(int i = 0; i < (int)SPEC.COUNT; i++)
      for(int j = 0; j < (int)SCENE.COUNT; j++)
        layer_names[i,j] = "Set_"+scene_names[j]+"_"+spec_names[i];

    scene_groups = new GameObject[(int)SPEC.COUNT,(int)SCENE.COUNT];
    for(int i = 0; i < (int)SPEC.COUNT; i++)
      for(int j = 0; j < (int)SCENE.COUNT; j++)
        scene_groups[i,j] = GameObject.Find(layer_names[i,j]);

    layers = new int[(int)SPEC.COUNT,(int)SCENE.COUNT];
    for(int i = 0; i < (int)SPEC.COUNT; i++)
      for(int j = 0; j < (int)SCENE.COUNT; j++)
        layers[i,j] = LayerMask.NameToLayer(layer_names[i,j]);

    string[,] skybox_files = new string[(int)SPEC.COUNT,(int)SCENE.COUNT];
    for(int i = 0; i < (int)SPEC.COUNT; i++)
      for(int j = 0; j < (int)SCENE.COUNT; j++)
        skybox_files[i,j] = spec_names[i]+"/"+skybox_file_names[j];

    default_layer = LayerMask.NameToLayer("Default");

    camera_house       = GameObject.Find("CameraHouse");
    main_camera        = GameObject.Find("Main Camera");
    main_camera_skybox = main_camera.GetComponent<Skybox>();
    portal_projection  = GameObject.Find("Portal_Projection");
    portal             = GameObject.Find("Portal");
    portal_camera_next = GameObject.Find("Portal_Camera_Next");
    portal_camera_next_skybox = portal_camera_next.GetComponent<Skybox>();
    helmet             = GameObject.Find("Helmet");
    helmet_light       = GameObject.Find("Helmet_Light");
    helmet_light_light = helmet_light.GetComponent<Light>();
    cam_reticle        = GameObject.Find("Cam_Reticle");
    cam_spinner        = GameObject.Find("Cam_Spinner");
    gaze_projection    = GameObject.Find("Gaze_Projection");
    gaze_reticle       = GameObject.Find("Gaze_Reticle");
    spec_projection    = GameObject.Find("Spec_Projection");
    spec_viz_reticle   = GameObject.Find("Spec_Viz_Reticle");
    spec_gam_reticle   = GameObject.Find("Spec_Gam_Reticle");
    spec_neu_reticle   = GameObject.Find("Spec_Neu_Reticle");
    spec_sel_reticle   = GameObject.Find("Spec_Sel_Reticle");
    eyeray             = GameObject.Find("Ray");
    ar_camera_project  = GameObject.Find("AR_Camera_Project");
    ar_camera_static   = GameObject.Find("AR_Camera_Static");
    ar_circle          = GameObject.Find("ARCircle");
    ar_label           = GameObject.Find("ARLabel");
    ar_label_offset    = GameObject.Find("ARLabelOffset");
    ar_label_text      = ar_label.GetComponent<TextMesh>();
    ar_alert           = GameObject.Find("Alert");
    ar_timer           = GameObject.Find("Timer");
    ar_timer_text      = ar_timer.GetComponent<TextMesh>();
    stars = GameObject.Find("Stars");
    starsscale = GameObject.Find("StarsScale");

    ar_alert.SetActive(false);
    ar_timer.SetActive(false);

    icecube   = new GameObject[(int)SPEC.COUNT];
    voyager   = new GameObject[(int)SPEC.COUNT];
    milky     = new GameObject[(int)SPEC.COUNT];
    blackhole = new GameObject[(int)SPEC.COUNT];
    earth     = new GameObject[(int)SPEC.COUNT];
    for(int i = 0; i < (int)SPEC.COUNT; i++)
    {
      icecube[i]   = GameObject.Find("Icecube_"+spec_names[i]);
      voyager[i]   = GameObject.Find("Voyager_"+spec_names[i]);
      milky[i]     = GameObject.Find("Milky_"+spec_names[i]);
      blackhole[i] = GameObject.Find("BlackHole_"+spec_names[i]);
      earth[i]     = GameObject.Find("Earth_"+spec_names[i]);
    }

    scene_centers[(int)SCENE.ICE]     = icecube[0].transform.position;
    scene_centers[(int)SCENE.VOYAGER] = voyager[0].transform.position;
    scene_centers[(int)SCENE.NOTHING] = milky[0].transform.position;
    scene_centers[(int)SCENE.EXTREME] = blackhole[0].transform.position;
    scene_centers[(int)SCENE.EARTH]   = earth[0].transform.position;

    alpha_id = Shader.PropertyToID("alpha");
    flash_alpha = 0;
    track_source = GameObject.Find("Script").AddComponent<AudioSource>();
    sfx_source   = GameObject.Find("Script").AddComponent<AudioSource>();

    default_portal_scale = portal.transform.localScale;
    default_portal_position = portal.transform.position;

    default_satellite_position = new Vector3(4f,1.5f,10);
    satellite_position = default_satellite_position;
    satellite_velocity = new Vector3(0,0,-0.5f);
    voyager[0].transform.position = satellite_position;

    default_look_ahead = new Vector3(0,0,1);
    look_ahead = default_look_ahead;
    lazy_look_ahead = default_look_ahead;
    very_lazy_look_ahead = default_look_ahead;
    player_head = new Vector3(0,2,0);

    cur_scene_i = 0;
    next_scene_i = cur_scene_i+1;
    cur_spec_i = 0;

    mouse_captured = false;
    mouse_just_captured = true;
    mouse_x = Screen.width/2;
    mouse_y = Screen.height/2;

    camera_house.transform.rotation = Quaternion.Euler((mouse_y-Screen.height/2)*-2, (mouse_x-Screen.width/2)*2, 0);

    in_portal_motion = 0;
    out_portal_motion = 0;
    max_portal_motion = 1;

    gaze_t_max = 2;
    gaze_t_since = 0;
    gaze_t_in = 0;
    gaze_t_run = 0;
    gaze_t_numb = 0;

    spec_t_max = 0.01f;
    spec_t_since = 0;
    spec_t_in = 0;
    spec_t_run = 0;
    spec_t_numb = 0;

    gaze_pt = new Vector3(1f,.8f,-1f).normalized;

    gaze_pt *= 1000;
    cam_euler = getCamEuler(cam_reticle.transform.position);
    gaze_cam_euler = getCamEuler(gaze_pt);
    anti_gaze_pt = (gaze_pt*-1)+new Vector3(50f,0,0);

    eyeray.GetComponent<LineRenderer>().SetPosition(0,anti_gaze_pt);
    eyeray.GetComponent<LineRenderer>().SetPosition(1,gaze_pt);

    gaze_projection.transform.rotation = rotationFromEuler(gaze_cam_euler);
    portal_projection.transform.rotation = rotationFromEuler(gaze_cam_euler);

    spec_euler = cam_euler;
    spec_euler.x = -3.141592f/3f;
    spec_projection.transform.rotation = rotationFromEuler(spec_euler);

    skyboxes = new Material[(int)SPEC.COUNT,(int)SCENE.COUNT];
    for(int i = 0; i < (int)SPEC.COUNT; i++)
      for(int j = 0; j < (int)SCENE.COUNT; j++)
      skyboxes[i,j] = Resources.Load<Material>(skybox_files[i,j]);
    main_camera_skybox.material        = skyboxes[cur_spec_i,cur_scene_i];
    portal_camera_next_skybox.material = skyboxes[cur_spec_i,next_scene_i];

    audio_clips = new AudioClip[audio_files.Length];
    for(int i = 0; i < audio_files.Length; i++)
      audio_clips[i] = Resources.Load<AudioClip>(audio_files[i]);
    cur_audio_playing_i = -1;
    cur_audio_playing_section = 0;

    ar_circle.transform.position = icecube[0].transform.position;
    ar_circle.transform.rotation = rotationFromEuler(getCamEuler(ar_circle.transform.position));
    ar_circle.transform.localScale = new Vector3(200,200,200);
    ar_label_text.text = "Ice Cube";
    ar_label.transform.localScale = new Vector3(10,10,10);

    helmet_light_light.color = scene0_helmet_color;

    //stars
    GameObject[] star_groups;
    GameObject star;
    Vector3[] star_positions;
    Vector3 starpos;

    int n_stars = 0;//100000;
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
      good_star = (starpos.sqrMagnitude < 1f);
      while(!good_star)
      {
        starpos = new Vector3(Random.Range(-1f,1f),Random.Range(-1f,1f),Random.Range(-1f,1f));
        good_star = (starpos.sqrMagnitude < 1f);
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
        starpos *= Mathf.Lerp(0f,30f,Random.Range(0f,1f));

        star.transform.position = starpos;
        star.transform.rotation = Quaternion.Euler(Random.Range(0f,360f),Random.Range(0f,360f),Random.Range(0f,360f));
        float ss = 0.1f;
        star.transform.localScale = new Vector3(ss,ss,ss);

        combine[j].mesh = star.transform.GetComponent<MeshFilter>().mesh;
        combine[j].transform = star.transform.localToWorldMatrix;
      }

      star_groups[i] = (GameObject)Instantiate(star_prefab);
      star_groups[i].transform.parent = starsscale.transform;
      //star_groups[i].layer = stars_layer;
      star_groups[i].transform.localPosition = new Vector3(0,0,0);
      star_groups[i].transform.localRotation = Quaternion.Euler(0,0,0);
      star_groups[i].transform.localScale = new Vector3(1,1,1);
      star_groups[i].transform.GetComponent<MeshFilter>().mesh = new Mesh();
      star_groups[i].transform.GetComponent<MeshFilter>().mesh.CombineMeshes(combine);

      n_stars -= n_stars_in_group;
    }
    Destroy(star,0f);
  }

  void Update()
  {
    float aspect = main_camera.GetComponent<Camera>().aspect;
    float fov = main_camera.GetComponent<Camera>().fieldOfView;
    ar_camera_project.GetComponent<Camera>().aspect = aspect;
    ar_camera_static.GetComponent<Camera>().aspect = aspect;
    portal_camera_next.GetComponent<Camera>().aspect = aspect;
    ar_camera_project.GetComponent<Camera>().fieldOfView = fov;
    ar_camera_static.GetComponent<Camera>().fieldOfView = fov;
    portal_camera_next.GetComponent<Camera>().fieldOfView = fov;

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

    if(in_portal_motion > 0) in_portal_motion += Time.deltaTime;
    if(in_portal_motion > max_portal_motion)
    {
      in_portal_motion = 0;
      out_portal_motion = Time.deltaTime;
      cur_scene_i = next_scene_i;
      next_scene_i = (next_scene_i+1)%((int)SCENE.COUNT);

      main_camera.GetComponent<Camera>().cullingMask        = (1 << layers[cur_spec_i,cur_scene_i]) | (1 << default_layer);
      portal_camera_next.GetComponent<Camera>().cullingMask = (1 << layers[cur_spec_i,next_scene_i]);

      main_camera_skybox.material = skyboxes[cur_spec_i,cur_scene_i];

      switch(cur_scene_i)
      {
        case 0:
          //See Start Function
          break;
        case 1:
          ar_circle.transform.position = voyager[0].transform.position;
          ar_circle.transform.rotation = rotationFromEuler(getCamEuler(ar_circle.transform.position));
          ar_circle.transform.localScale = new Vector3(5,5,5);
          ar_label_text.text = "Voyager";
          ar_label.transform.localScale = new Vector3(0.5f,0.5f,0.5f);
          helmet_light_light.color = scene1_helmet_color;
          break;
        case 2:
          ar_circle.transform.position = milky[0].transform.position;
          ar_circle.transform.rotation = rotationFromEuler(getCamEuler(ar_circle.transform.position));
          ar_circle.transform.localScale = new Vector3(50,50,50);
          ar_label_text.text = "Milky Way";
          ar_label.transform.localScale = new Vector3(2,2,2);
          helmet_light_light.color = scene2_helmet_color;
          break;
        case 3:
          ar_circle.transform.position = blackhole[0].transform.position;
          ar_circle.transform.rotation = rotationFromEuler(getCamEuler(ar_circle.transform.position));
          ar_circle.transform.localScale = new Vector3(200,200,200);
          ar_label_text.text = "Black Hole";
          ar_label.transform.localScale = new Vector3(10,10,10);
          ar_alert.SetActive(true);
          ar_timer.SetActive(true);
          timer_t = 0;
          alert_t = 0;
          helmet_light_light.color = scene3_helmet_color;
          break;
        case 4:
          ar_circle.transform.position = earth[0].transform.position;
          ar_circle.transform.rotation = rotationFromEuler(getCamEuler(ar_circle.transform.position));
          ar_circle.transform.localScale = new Vector3(100,100,100);
          ar_label_text.text = "Earth";
          ar_label.transform.localScale = new Vector3(5,5,5);
          ar_alert.SetActive(false);
          ar_timer.SetActive(false);
          helmet_light_light.color = scene4_helmet_color;
          break;
      }
      ar_label_offset.transform.localScale = ar_circle.transform.localScale;
      ar_label.transform.localScale /= ar_circle.transform.localScale.x;
    }
    if(out_portal_motion > 0) out_portal_motion += Time.deltaTime;
    if(out_portal_motion > max_portal_motion)
    {
      out_portal_motion = 0;
    }

    if(in_portal_motion > 0)
    {
      float t = in_portal_motion/(float)max_portal_motion;
      portal.transform.localPosition = new Vector3(default_portal_position.x,default_portal_position.y,Mathf.Lerp(default_portal_position.z,0,t*t*t*t*t));
      float engulf = t-1;
      engulf *= -engulf;
      engulf += 1;
      engulf /= 2;
      portal.transform.localScale = new Vector3(default_portal_scale.x*engulf,default_portal_scale.y*engulf,default_portal_scale.z*engulf);
    }
    else
    {
      portal.transform.localPosition = default_portal_position;
      portal.transform.localScale = new Vector3(0,0,0);
    }

    switch(cur_scene_i)
    {
      case 0:
        break;
      case 1:
        ar_circle.transform.position = voyager[0].transform.position;
        ar_circle.transform.rotation = rotationFromEuler(getCamEuler(ar_circle.transform.position));
        for(int i = 1; i < voyager.Length; i++)
        {
          voyager[i].transform.position = voyager[0].transform.position;
        }
        break;
      case 2:
        break;
      case 3:
        alert_t += Time.deltaTime;
        timer_t += Time.deltaTime;
        if(Mathf.Floor(alert_t)%2 == 1) ar_alert.SetActive(false);
        else                            ar_alert.SetActive(true);
        float seconds_left = 20-timer_t;
        if(seconds_left > 0)
        {
          ar_timer_text.text = "00:"+Mathf.Floor(seconds_left)+":"+Mathf.Floor((seconds_left-Mathf.Floor(seconds_left))*100);
        }
        else
        {
          ar_timer_text.text = "00:00:00";
        }
        break;
      case 4:
        break;
    }
    ar_label_offset.transform.position = ar_circle.transform.position;
    ar_label_offset.transform.rotation = ar_circle.transform.rotation;

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
    very_lazy_look_ahead = Vector3.Lerp(very_lazy_look_ahead,look_ahead,0.01f);
    helmet.transform.position = main_camera.transform.position;
    helmet.transform.rotation = rotationFromEuler(getEuler(lazy_look_ahead));

    cam_euler = getCamEuler(cam_reticle.transform.position);
    spec_euler = getEuler(very_lazy_look_ahead);
    spec_euler.x = -3.141592f/3f;
    spec_projection.transform.rotation = rotationFromEuler(spec_euler);

    satellite_position += satellite_velocity*Time.deltaTime;
    if(cur_scene_i != 1) satellite_position = default_satellite_position;
    voyager[0].transform.position = satellite_position;

    if(in_portal_motion > 0)
    {
      float t = (float)in_portal_motion/max_portal_motion;
      flash_alpha = t*t*t*t*t;
    }
    else if(out_portal_motion > 0)
    {
      flash_alpha = 1.0f-((float)out_portal_motion/max_portal_motion);
    }
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

    cam_spinner.transform.localScale = new Vector3(shrink,shrink,shrink);
    cam_spinner.transform.localRotation = Quaternion.Euler(0,0,rot);

    float distance = Vector3.Distance(gaze_reticle.transform.position,cam_reticle.transform.position);
    if(gaze_t_numb <= 0 && distance < 0.3)
    {
      if(gaze_t_since < 0) gaze_t_since = Time.deltaTime;
      else                 gaze_t_since += Time.deltaTime;
      if(gaze_t_in < gaze_t_max) gaze_t_in += Time.deltaTime;
      if(gaze_t_in >= gaze_t_max) gaze_t_numb = gaze_t_max*2;

      //advance
      if(gaze_t_in >= gaze_t_max)
      {
        if(in_portal_motion == 0 && out_portal_motion == 0)
        {
          in_portal_motion = Time.deltaTime;
          scene_rots[next_scene_i] = 0; //set rot at start of scene transition
          portal_camera_next_skybox.material = skyboxes[cur_spec_i,next_scene_i];
        }
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
      if(gaze_t_since > 0) gaze_t_since = -Time.deltaTime;
      else                 gaze_t_since -= Time.deltaTime;
      if(gaze_t_in > 0) gaze_t_in -= Time.deltaTime;
    }
    if(gaze_t_in > 0) gaze_t_run += Time.deltaTime;
    else              gaze_t_run = 0;
    if(gaze_t_numb > 0) gaze_t_numb -= Time.deltaTime;

    float distance_viz = Vector3.Distance(spec_viz_reticle.transform.position,cam_reticle.transform.position);
    float distance_gam = Vector3.Distance(spec_gam_reticle.transform.position,cam_reticle.transform.position);
    float distance_neu = Vector3.Distance(spec_neu_reticle.transform.position,cam_reticle.transform.position);
    if(spec_t_numb <= 0 && (distance_gam < 0.3 || distance_viz < 0.3 || distance_neu < 0.3))
    {
      if(spec_t_since < 0) spec_t_since = Time.deltaTime;
      else                 spec_t_since += Time.deltaTime;
      if(spec_t_in < spec_t_max) spec_t_in += Time.deltaTime;
      if(spec_t_in >= spec_t_max) spec_t_numb = spec_t_max*2;

      if(spec_t_in >= spec_t_max)
      {
        if(distance_gam <= distance_viz && distance_gam <= distance_neu)
        {
          cur_spec_i = (int)SPEC.GAM;
          spec_sel_reticle.transform.position = spec_gam_reticle.transform.position;
        }
        if(distance_viz <= distance_gam && distance_viz <= distance_neu)
        {
          cur_spec_i = (int)SPEC.VIZ;
          spec_sel_reticle.transform.position = spec_viz_reticle.transform.position;
        }
        if(distance_neu <= distance_gam && distance_neu <= distance_viz)
        {
          cur_spec_i = (int)SPEC.NEU;
          spec_sel_reticle.transform.position = spec_neu_reticle.transform.position;
        }
        main_camera.GetComponent<Camera>().cullingMask        = (1 << layers[cur_spec_i,cur_scene_i]) | (1 << default_layer);
        portal_camera_next.GetComponent<Camera>().cullingMask = (1 << layers[cur_spec_i,next_scene_i]);
        main_camera_skybox.material        = skyboxes[cur_spec_i,cur_scene_i];
        portal_camera_next_skybox.material = skyboxes[cur_spec_i,next_scene_i];
      }
    }
    else
    {
      if(spec_t_since > 0) spec_t_since = -Time.deltaTime;
      else                 spec_t_since -= Time.deltaTime;
      if(spec_t_in > 0) spec_t_in -= Time.deltaTime;
    }
    if(spec_t_in > 0) spec_t_run += Time.deltaTime;
    else              spec_t_run = 0;
    if(spec_t_numb > 0) spec_t_numb -= Time.deltaTime;

    scene_rots[cur_scene_i] += scene_rot_deltas[cur_scene_i]*Time.deltaTime;
    while(scene_rots[cur_scene_i] > 3.14159265f*2.0f)
      scene_rots[cur_scene_i] -= (3.14159265f*2.0f);
    scene_rots[next_scene_i] += scene_rot_deltas[next_scene_i]*Time.deltaTime;
    while(scene_rots[next_scene_i] > 3.14159265f*2.0f)
      scene_rots[next_scene_i] -= (3.14159265f*2.0f);

    //Matrix4x4 m = Matrix4x4.Translate(-scene_centers[cur_scene_i]);
    //m *= Matrix4x4.Rotate(Quaternion.Euler(0f, Mathf.Rad2Deg*scene_rots[cur_scene_i], 0f));
    //m *= Matrix4x4.Translate(scene_centers[cur_scene_i]);
    //scene_groups[cur_spec_i,cur_scene_i].transform.localToWorldMatrix = m;

    scene_groups[cur_spec_i,cur_scene_i].transform.position = new Vector3(0,0,0);
    scene_groups[cur_spec_i,cur_scene_i].transform.rotation = Quaternion.Euler(0f, 0f, 0f);

    scene_groups[cur_spec_i,cur_scene_i].transform.Translate(scene_centers[cur_scene_i]);
    scene_groups[cur_spec_i,cur_scene_i].transform.Rotate(0f, Mathf.Rad2Deg*scene_rots[cur_scene_i], 0f);
    scene_groups[cur_spec_i,cur_scene_i].transform.Translate(-scene_centers[cur_scene_i]);
    main_camera_skybox.material.SetFloat("_Rotation", -Mathf.Rad2Deg*scene_rots[cur_scene_i]);

    //scene_groups[cur_spec_i,cur_scene_i].transform.rotation = Quaternion.Euler(0f, Mathf.Rad2Deg*scene_rots[cur_scene_i], 0f);
    //scene_groups[cur_spec_i,cur_scene_i].transform.position = -scene_centers[cur_scene_i];

    //scene_groups[cur_spec_i,cur_scene_i].transform.position = new Vector3(Mathf.Cos(cur_movement_theta)*10.0f,0.0f,Mathf.Sin(cur_movement_theta)*10.0f);
    //stars.transform.position = new Vector3(Mathf.Cos(cur_movement_theta)*10.0f,0.0f,Mathf.Sin(cur_movement_theta)*10.0f);

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

