using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Main : MonoBehaviour
{

  public class gaze_trigger
  {
    public Vector3 position = new Vector3(0f,0f,0f);
    public float t_max         = 1f; //The time required to confirm a gaze
    public float t_max_numb    = 1f; //The time required to wait before confirming another gaze

    public float t_in          = 0f; //grows to max when in, 0 when out
    public float t_numb        = 0f; //countdown when distance should be ignored
    public bool just_in        = false; //1 when newly in
    public bool just_triggered = false; //1 when in for > threshhold
    public float range         = 0.3f; //required distance for activation

    //derived
    public float shrink = 0f;
    public float rot    = 0f;

    public gaze_trigger()
    {}

    public bool in_range(Vector3 ptr)
    {
      return Vector3.Distance(this.position,ptr) < this.range;
    }

    public bool tick(Vector3 ptr, float dt)
    {
      this.just_in = false;
      this.just_triggered = false;

      bool ret = false;
      if(this.t_numb <= 0 && this.in_range(ptr))
      {
        if(this.t_in == 0) this.just_in = true; //breaking news!!
        this.t_in += dt;
        if(this.t_in >= this.t_max)
        {
          this.just_triggered = true;
          this.t_numb = this.t_max_numb;
          this.t_in = this.t_max;
        }
        ret = true;
      }
      else
      {
        this.t_in = 0;
        this.t_numb -= dt;
        ret = false;
      }

      this.shrink = (this.t_in/this.t_max);
      this.rot    = (this.t_in/this.t_max)*5f*360.0f;

      return ret;
    }

    public void reset()
    {
      this.t_in           = 0f;
      this.t_numb         = 0f;
      this.just_in        = false;
      this.just_triggered = false;
    }

  }

  public string[] credit_strings = new string[]
  {
    "", //needs empty string at beginning so it starts off empty
    "Me",
    "You",
    "Him",
    "Her",
    "Them",
    "Everybody",
    "Else",
    "", //needs empty string at end so it eventually shuts up
  };
  int credits_i;
  float credits_t;
  float max_credits_t = 5f;

  float twopi = 3.14159265f*2f;

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
  float reticle_d;
  GameObject gaze_projection;
  GameObject gaze_reticle;
  GameObject spec_projection;
  GameObject spec_viz_reticle;
  GameObject spec_gam_reticle;
  GameObject spec_neu_reticle;
  GameObject spec_sel_reticle;
  GameObject gazeray;
  GameObject gazeball;
  GameObject grid;
  GameObject ar_group;
  GameObject ar_camera_project;
  GameObject ar_camera_static;
  GameObject[] ar_maps;
  GameObject ar_alert;
  GameObject ar_timer;
  TextMesh ar_timer_text;
  GameObject credits_0;
  GameObject credits_1;
  TextMesh credits_text_0;
  TextMesh credits_text_1;
  GameObject subtitles;
  TextMesh subtitles_text;

  int MAX_LABELS = 5;
  GameObject[] ar_label_lefts;
  GameObject[] ar_label_left_kids;
  GameObject[] ar_label_left_quads;
  TextMesh[] ar_label_left_texts;
  GameObject[] ar_label_rights;
  GameObject[] ar_label_right_kids;
  GameObject[] ar_label_right_quads;
  TextMesh[] ar_label_right_texts;
  GameObject[] ar_label_bhs;
  GameObject[] ar_label_bh_kids;
  GameObject[] ar_label_bh_quads;
  TextMesh[] ar_label_bh_texts;
  GameObject[] ar_progresses;
  GameObject[] ar_progress_offsets;
  LineRenderer[] ar_progress_lines;
  GameObject[] ar_label_checks;
  GameObject[] ar_spec_checks;

  GameObject[] icecube;
  GameObject[] pluto;
  GameObject[] vearth;
  GameObject[] milky;
  GameObject[] nearth;
  GameObject[] blackhole;
  GameObject[] esun;
  GameObject[] earth;
  //GameObject stars;
  //GameObject starsscale;

  int grid_w = 10;
  int grid_h = 10;
  int grid_d = 10;
  float grid_oyoff = 0f;
  float grid_yoff = -1000f;
  float grid_yvel = 0f;
  float grid_yacc = 0f;
  float default_grid_s;
  float[,,] grid_s;
  GameObject[,,] grid_bulbs;

  Vector3 default_portal_scale;
  Vector3 default_portal_position;
  Vector3 default_look_ahead;
  Vector3 look_ahead;
  Vector3 lazy_look_ahead;
  Vector3 very_lazy_look_ahead;
  Vector3 player_head;

  public int starting_scene = 0;

  public Material alpha_material;
  public GameObject star_prefab;
  public GameObject ar_label_left_prefab;
  public GameObject ar_label_right_prefab;
  public GameObject ar_label_bh_prefab;
  public GameObject ar_progress_prefab;
  public GameObject grid_string_prefab;
  public GameObject grid_bulb_prefab;
  public GameObject ar_check_prefab;

  public Color scene0_helmet_color;
  public Color scene1_helmet_color;
  public Color scene2_helmet_color;
  public Color scene3_helmet_color;
  public Color scene4_helmet_color;
  Color[] helmet_colors; //wrangle into array in Start for easier accessibility

  public float extreme_camera_shake = 0.2f;
  public float extreme_helmet_shake = 0.001f;

  [Range(0.0f, 1.0f)]
  public float ice_viz_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float ice_gam_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float ice_neu_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float ice_end_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float voyager_viz_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float voyager_gam_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float voyager_neu_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float voyager_end_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float nothing_viz_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float nothing_gam_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float nothing_neu_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float nothing_end_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float extreme_viz_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float extreme_gam_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float extreme_neu_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float extreme_end_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float earth_viz_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float earth_gam_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float earth_neu_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float earth_end_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float credits_viz_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float credits_gam_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float credits_neu_voice_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float credits_end_voice_vol = 1.0f;

  [Range(0.0f, 1.0f)]
  public float ice_viz_music_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float ice_gam_music_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float ice_neu_music_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float voyager_viz_music_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float voyager_gam_music_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float voyager_neu_music_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float nothing_viz_music_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float nothing_gam_music_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float nothing_neu_music_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float extreme_viz_music_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float extreme_gam_music_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float extreme_neu_music_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float earth_viz_music_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float earth_gam_music_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float earth_neu_music_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float credits_viz_music_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float credits_gam_music_vol = 1.0f;
  [Range(0.0f, 1.0f)]
  public float credits_neu_music_vol = 1.0f;

  public GoogleAnalyticsV4 ga;

  int alpha_id;
  float flash_alpha;
  int time_mod_twelve_pi_id;
  float time_mod_twelve_pi;
  int jitter_id;
  float jitter;
  float jitter_countdown;
  int jitter_state;
  float jitter_min_downtime = 0.1f;
  float jitter_max_downtime = 1f;
  float jitter_min_uptime = 0.1f;
  float jitter_max_uptime = 0.4f;

  float alert_t;
  float timer_t;

  AudioSource voiceover_audiosource;
  bool voiceover_was_playing;
  string[,] voiceover_files;
  AudioClip[,] voiceovers;
  float[,] voiceover_vols;
  int MAX_SUBTITLES_PER_CLIP = 50;
  string[,,] subtitle_strings;
  float[,,] subtitle_cues_delta;
  float[,,] subtitle_cues_absolute;
  int subtitle_i;
  float subtitle_t;
  int subtitle_spec;
  int subtitle_pause_i_ice_0 = 0;
  int subtitle_pause_i_ice_1 = 0;
  bool advance_passed_ice_0 = false;
  bool advance_passed_ice_1 = false;
  int subtitle_pause_i_voyager_0 = 0;
  int subtitle_pause_i_voyager_1 = 0;
  bool advance_passed_voyager_0 = false;
  bool advance_passed_voyager_1 = false;
  bool advance_paused = false;
  bool hmd_mounted = false;
  bool[,] voiceovers_played;
  AudioSource music_audiosource;
  bool music_was_playing;
  string[,] music_files;
  AudioClip[,] musics;
  float[,] music_vols;
  AudioSource[] sfx_audiosource;
  AudioSource warp_audiosource_ptr; //<- PTR! DON'T ALLOCATE!
  int n_sfx_audiosources;
  int sfx_audiosource_i;
  string[] sfx_files = new string[]
  {
    "sfx/alert_fast",
    "sfx/alert_slow",
    "sfx/complete",
    "sfx/select",
    "sfx/warp",
    "sfx/warp_click"
  };
  //very clunky, but whatever
  enum SFX
  {
    ALERT_FAST,
    ALERT_SLOW,
    COMPLETE,
    SELECT,
    WARP,
    WARP_CLICK,
    COUNT
  };
  AudioClip[] sfxs;
  float[] sfx_vols;

  AudioSource PlaySFX(SFX s)
  {
    if(sfx_audiosource[sfx_audiosource_i].isPlaying) sfx_audiosource[sfx_audiosource_i].Stop();
    sfx_audiosource[sfx_audiosource_i].clip = sfxs[(int)s];
    sfx_audiosource[sfx_audiosource_i].volume = sfx_vols[(int)s];
    sfx_audiosource[sfx_audiosource_i].Play();
    AudioSource used_source = sfx_audiosource[sfx_audiosource_i];
    sfx_audiosource_i = (sfx_audiosource_i+1)%n_sfx_audiosources;
    return used_source;
  }

  void MapVols()
  {
    voiceover_vols[(int)SCENE.ICE,(int)SPEC.VIZ]       = ice_viz_voice_vol;
    voiceover_vols[(int)SCENE.ICE,(int)SPEC.GAM]       = ice_gam_voice_vol;
    voiceover_vols[(int)SCENE.ICE,(int)SPEC.NEU]       = ice_neu_voice_vol;
    voiceover_vols[(int)SCENE.ICE,(int)SPEC.COUNT]     = ice_end_voice_vol;
    voiceover_vols[(int)SCENE.VOYAGER,(int)SPEC.VIZ]   = voyager_viz_voice_vol;
    voiceover_vols[(int)SCENE.VOYAGER,(int)SPEC.GAM]   = voyager_gam_voice_vol;
    voiceover_vols[(int)SCENE.VOYAGER,(int)SPEC.NEU]   = voyager_neu_voice_vol;
    voiceover_vols[(int)SCENE.VOYAGER,(int)SPEC.COUNT] = voyager_end_voice_vol;
    voiceover_vols[(int)SCENE.NOTHING,(int)SPEC.VIZ]   = nothing_viz_voice_vol;
    voiceover_vols[(int)SCENE.NOTHING,(int)SPEC.GAM]   = nothing_gam_voice_vol;
    voiceover_vols[(int)SCENE.NOTHING,(int)SPEC.NEU]   = nothing_neu_voice_vol;
    voiceover_vols[(int)SCENE.NOTHING,(int)SPEC.COUNT] = nothing_end_voice_vol;
    voiceover_vols[(int)SCENE.EXTREME,(int)SPEC.VIZ]   = extreme_viz_voice_vol;
    voiceover_vols[(int)SCENE.EXTREME,(int)SPEC.GAM]   = extreme_gam_voice_vol;
    voiceover_vols[(int)SCENE.EXTREME,(int)SPEC.NEU]   = extreme_neu_voice_vol;
    voiceover_vols[(int)SCENE.EXTREME,(int)SPEC.COUNT] = extreme_end_voice_vol;
    voiceover_vols[(int)SCENE.EARTH,(int)SPEC.VIZ]     = earth_viz_voice_vol;
    voiceover_vols[(int)SCENE.EARTH,(int)SPEC.GAM]     = earth_gam_voice_vol;
    voiceover_vols[(int)SCENE.EARTH,(int)SPEC.NEU]     = earth_neu_voice_vol;
    voiceover_vols[(int)SCENE.EARTH,(int)SPEC.COUNT]   = earth_end_voice_vol;
    voiceover_vols[(int)SCENE.CREDITS,(int)SPEC.VIZ]   = credits_viz_voice_vol;
    voiceover_vols[(int)SCENE.CREDITS,(int)SPEC.GAM]   = credits_gam_voice_vol;
    voiceover_vols[(int)SCENE.CREDITS,(int)SPEC.NEU]   = credits_neu_voice_vol;
    voiceover_vols[(int)SCENE.CREDITS,(int)SPEC.COUNT] = credits_end_voice_vol;

    music_vols[(int)SCENE.ICE,(int)SPEC.VIZ]       = ice_viz_music_vol;
    music_vols[(int)SCENE.ICE,(int)SPEC.GAM]       = ice_gam_music_vol;
    music_vols[(int)SCENE.ICE,(int)SPEC.NEU]       = ice_neu_music_vol;
    music_vols[(int)SCENE.VOYAGER,(int)SPEC.VIZ]   = voyager_viz_music_vol;
    music_vols[(int)SCENE.VOYAGER,(int)SPEC.GAM]   = voyager_gam_music_vol;
    music_vols[(int)SCENE.VOYAGER,(int)SPEC.NEU]   = voyager_neu_music_vol;
    music_vols[(int)SCENE.NOTHING,(int)SPEC.VIZ]   = nothing_viz_music_vol;
    music_vols[(int)SCENE.NOTHING,(int)SPEC.GAM]   = nothing_gam_music_vol;
    music_vols[(int)SCENE.NOTHING,(int)SPEC.NEU]   = nothing_neu_music_vol;
    music_vols[(int)SCENE.EXTREME,(int)SPEC.VIZ]   = extreme_viz_music_vol;
    music_vols[(int)SCENE.EXTREME,(int)SPEC.GAM]   = extreme_gam_music_vol;
    music_vols[(int)SCENE.EXTREME,(int)SPEC.NEU]   = extreme_neu_music_vol;
    music_vols[(int)SCENE.EARTH,(int)SPEC.VIZ]     = earth_viz_music_vol;
    music_vols[(int)SCENE.EARTH,(int)SPEC.GAM]     = earth_gam_music_vol;
    music_vols[(int)SCENE.EARTH,(int)SPEC.NEU]     = earth_neu_music_vol;
    music_vols[(int)SCENE.CREDITS,(int)SPEC.VIZ]   = credits_viz_music_vol;
    music_vols[(int)SCENE.CREDITS,(int)SPEC.GAM]   = credits_gam_music_vol;
    music_vols[(int)SCENE.CREDITS,(int)SPEC.NEU]   = credits_neu_music_vol;

    if(voiceover_was_playing)
      voiceover_audiosource.volume = voiceover_vols[cur_scene_i,cur_spec_i];
    if(music_was_playing)
      music_audiosource.volume = music_vols[cur_scene_i,cur_spec_i];
  }

  string[,] skybox_files;
  Material[,] skyboxes;

  Vector3[] scene_centers = new Vector3[]
  {
    new Vector3(0f, 0f, 0f), //ice
    new Vector3(0f, 0f, 0f), //voyager
    new Vector3(0f, 0f, 0f), //nothing
    new Vector3(0f, 0f, 0f), //extreme
    new Vector3(0f, 0f, 0f), //earth
    new Vector3(0f, 0f, 0f), //credits
  };
  float[] scene_rots = new float[]
  {
    0f, //ice
    0f, //voyager
    0f, //nothing
    0f, //extreme
    0f, //earth
    0f, //credits
  };
  float[] scene_rot_deltas = new float[]
  {
    0.0f, //ice
    0.0f, //voyager
    0.01f, //nothing
    0.002f, //extreme
    0.00f, //earth
    0.0f, //credits
  };

  enum SCENE
  {
    ICE,
    VOYAGER,
    NOTHING,
    EXTREME,
    EARTH,
    CREDITS,
    COUNT
  };

  enum SPEC
  {
    VIZ,
    GAM,
    NEU,
    COUNT
  };

  int cur_scene_i;
  int next_scene_i;
  int cur_spec_i;
  int[,] layers;
  string[] spec_names;
  string[] scene_names;
  string[,] layer_names;
  GameObject[,] scene_groups;
  float[,] ta; //"time alive" (timed amt in scene/spectrum pairs)
  int[] blackhole_spec_triggered;
  float scan_t = 5f; //time required before you can consider a spectrum "scanned"
  int default_layer;

  bool mouse_captured;
  bool mouse_just_captured;
  float mouse_x;
  float mouse_y;

  float in_portal_motion;
  float out_portal_motion;
  float max_portal_motion;

  float in_fail_motion;
  float out_fail_motion;
  float max_fail_motion;

  gaze_trigger advance_trigger;
  gaze_trigger spec_trigger;
  gaze_trigger warp_trigger;
  gaze_trigger blackhole_trigger;

  float dumb_delay_t_max; //prevent anything interesting til this point
  float dumb_delay_t;

  Vector3 gaze_pt;
  Vector3 anti_gaze_pt;
  Vector2 cam_euler;
  Vector2 gaze_cam_euler;
  Vector2 anti_gaze_cam_euler;
  Vector2 spec_euler;

  Vector2 getEuler(Vector3 v)
  {
    float plane_dist = new Vector2(v.x, v.z).magnitude;
    return new Vector2(Mathf.Atan2(v.y, plane_dist), -1 * (Mathf.Atan2(v.z, v.x) - Mathf.PI / 2));
  }

  Vector2 getCamEuler(Vector3 v)
  {
    return getEuler(v - main_camera.transform.position);
  }

  Quaternion rotationFromEuler(Vector2 euler)
  {
    return Quaternion.Euler(-Mathf.Rad2Deg * euler.x, Mathf.Rad2Deg * euler.y, 0);
  }


  void HandleHMDMounted()
  {
    hmd_mounted = true;
    ga.StopSession();
  }

  void HandleHMDUnmounted()
  {
    reStart();
    SetSpec((int)SPEC.VIZ);
    SetupScene();
    hmd_mounted = false;
  }

  void reStart()
  {
    for(int i = 0; i < (int)SCENE.COUNT; i++)
    {
      for(int j = 0; j < (int)SPEC.COUNT+1; j++)
      {
        voiceovers_played[i,j] = false;
      }
    }
    voiceover_was_playing = false;
    music_was_playing = false;
    voiceover_audiosource.Stop();
    music_audiosource.Stop();

    subtitles_text.text = "";
    subtitle_i = 0;
    subtitle_t = 0;
    subtitle_spec = (int)SPEC.VIZ;

    grid_yoff = -1000f;
    grid_yvel = 0f;
    grid_yacc = 0f;
    grid.transform.position = new Vector3(grid.transform.position.x,grid_oyoff+grid_yoff,grid.transform.position.z);

    //auto skip these
    voiceovers_played[(int)SCENE.ICE,(int)SPEC.NEU]     = true;
    voiceovers_played[(int)SCENE.ICE,(int)SPEC.GAM]     = true;
    voiceovers_played[(int)SCENE.NOTHING,(int)SPEC.NEU] = true;
    voiceovers_played[(int)SCENE.NOTHING,(int)SPEC.GAM] = true;
    voiceovers_played[(int)SCENE.EARTH,(int)SPEC.VIZ]   = true;
    voiceovers_played[(int)SCENE.EARTH,(int)SPEC.NEU]   = true;
    voiceovers_played[(int)SCENE.EARTH,(int)SPEC.GAM]   = true;
    voiceovers_played[(int)SCENE.CREDITS,(int)SPEC.NEU] = true;
    voiceovers_played[(int)SCENE.CREDITS,(int)SPEC.GAM] = true;

    for(int i = 0; i < (int)SCENE.COUNT; i++)
      for(int j = 0; j < (int)SPEC.COUNT; j++)
        ta[i,j] = 0;

    for(int i = 0; i < (int)SPEC.COUNT; i++)
      blackhole_spec_triggered[i] = 0;

    for(int i = 0; i < (int)SCENE.COUNT; i++)
      scene_rots[i] = 0f;

    flash_alpha = 0;
    time_mod_twelve_pi = 0;
    jitter = 0;
    jitter_countdown = 0;
    jitter_state = 0;

    next_scene_i = starting_scene;
    cur_scene_i = next_scene_i;
    next_scene_i = (next_scene_i + 1) % ((int)SCENE.COUNT);
    cur_spec_i = 0;

    mouse_captured = false;
    mouse_just_captured = true;
    mouse_x = Screen.width / 2;
    mouse_y = Screen.height / 2;

    in_portal_motion = 0;
    out_portal_motion = 0;
    max_portal_motion = 1;

    in_fail_motion = 0;
    out_fail_motion = 0;
    max_fail_motion = 1;

    dumb_delay_t_max = 3f;
    dumb_delay_t = 0f;

    credits_i = 0;
    credits_t = 0f;

    advance_paused = false;
    gaze_reticle.SetActive(false);
    gazeray.SetActive(false);
    gazeball.SetActive(false);
    ar_alert.SetActive(false);
    ar_timer.SetActive(false);

    advance_trigger.reset();
    spec_trigger.reset();
    warp_trigger.reset();
    blackhole_trigger.reset();

    ga.StartSession();
  }

  void Start()
  {
    OVRManager.HMDMounted += HandleHMDMounted;
    OVRManager.HMDUnmounted += HandleHMDUnmounted;
    Application.runInBackground = true;

    scene_names = new string[(int)SCENE.COUNT];
    for(int i = 0; i < (int)SCENE.COUNT; i++)
    {
      string name = "";
      switch(i)
      {
        case (int)SCENE.ICE:
          name = "Ice";
          break;
        case (int)SCENE.VOYAGER:
          name = "Voyager";
          break;
        case (int)SCENE.NOTHING:
          name = "Nothing";
          break;
        case (int)SCENE.EXTREME:
          name = "Extreme";
          break;
        case (int)SCENE.EARTH:
          name = "Earth";
          break;
        case (int)SCENE.CREDITS:
          name = "Credits";
          break;
      }
      scene_names[i] = name;
    }

    spec_names = new string[(int)SPEC.COUNT];
    for(int i = 0; i < (int)SPEC.COUNT; i++)
    {
      string name = "";
      switch(i)
      {
        case (int)SPEC.VIZ:
          name = "Viz";
          break;
        case (int)SPEC.GAM:
          name = "Gam";
          break;
        case (int)SPEC.NEU:
          name = "Neu";
          break;
      }
      spec_names[i] = name;
    }

    layer_names = new string[(int)SCENE.COUNT, (int)SPEC.COUNT];
    for(int i = 0; i < (int)SCENE.COUNT; i++)
      for(int j = 0; j < (int)SPEC.COUNT; j++)
        layer_names[i,j] = "Set_" + scene_names[i] + "_" + spec_names[j];

    scene_groups = new GameObject[(int)SCENE.COUNT, (int)SPEC.COUNT];
    for(int i = 0; i < (int)SCENE.COUNT; i++)
      for(int j = 0; j < (int)SPEC.COUNT; j++)
        scene_groups[i,j] = GameObject.Find(layer_names[i,j]);

    layers = new int[(int)SCENE.COUNT, (int)SPEC.COUNT];
    for(int i = 0; i < (int)SCENE.COUNT; i++)
      for(int j = 0; j < (int)SPEC.COUNT; j++)
        layers[i,j] = LayerMask.NameToLayer(layer_names[i,j]);

    voiceover_files = new string[(int)SCENE.COUNT,(int)SPEC.COUNT+1];
    for(int i = 0; i < (int)SCENE.COUNT; i++)
    {
      for(int j = 0; j < (int)SPEC.COUNT+1; j++)
      {
        if(j == (int)SPEC.COUNT) voiceover_files[i,j] = "Voiceover/End/"+scene_names[i];
        else                     voiceover_files[i,j] = "Voiceover/"+spec_names[j]+"/"+scene_names[i];
      }
    }
    voiceovers = new AudioClip[(int)SCENE.COUNT,(int)SPEC.COUNT+1];
    subtitle_strings       = new string[(int)SCENE.COUNT,(int)SPEC.COUNT+1,MAX_SUBTITLES_PER_CLIP];
    subtitle_cues_delta    = new float[ (int)SCENE.COUNT,(int)SPEC.COUNT+1,MAX_SUBTITLES_PER_CLIP];
    subtitle_cues_absolute = new float[ (int)SCENE.COUNT,(int)SPEC.COUNT+1,MAX_SUBTITLES_PER_CLIP];
    subtitle_i = 0;
    subtitle_t = 0;
    subtitle_spec = 0;
    voiceovers_played = new bool[(int)SCENE.COUNT,(int)SPEC.COUNT+1];
    voiceover_vols = new float[(int)SCENE.COUNT,(int)SPEC.COUNT+1];
    for(int i = 0; i < (int)SCENE.COUNT; i++)
    {
      for(int j = 0; j < (int)SPEC.COUNT+1; j++)
      {
        voiceovers[i,j] = Resources.Load<AudioClip>(voiceover_files[i,j]);
        voiceover_vols[i,j] = 1.0f;
      }
    }

    //manually fill out subtitles
    {
    int i;
    int j;
    int k;
    //prepopulate with defaults
    for(i = 0; i < (int)SCENE.COUNT; i++)
    for(j = 0; j < (int)SPEC.COUNT+1; j++)
    for(k = 0; k < MAX_SUBTITLES_PER_CLIP; k++)
    {
      subtitle_strings[i,j,k] = "";
      subtitle_cues_delta[ i,j,k] = 0.0001f;
      subtitle_cues_absolute[ i,j,k] = 0.0001f;
    }

    //ICE
    i = (int)SCENE.ICE;
    j = (int)SPEC.VIZ;
    k = 0;
    subtitle_strings[i,j,k] = "";
    subtitle_cues_delta[i,j,k] = 0.001f;
    k++;
    subtitle_strings[i,j,k] = "Hey! Come in!";
    subtitle_cues_delta[i,j,k] = 1f;//1
    k++;
    subtitle_strings[i,j,k] = "... Hello? ...";
    subtitle_cues_delta[i,j,k] = 1f;//2
    k++;
    subtitle_strings[i,j,k] = "Sorry, we're still getting the kinks worked out of this new suit.";
    subtitle_cues_delta[i,j,k] = 2.9f;//4
    k++;
    subtitle_strings[i,j,k] = "Let me know if this is working-";
    subtitle_cues_delta[i,j,k] = 1.5f;//6
    k++;
    subtitle_strings[i,j,k] = "I'm booting up the augmented reality overlay in your helmet now...";
    subtitle_cues_delta[i,j,k] = 4f;//10
    k++;
    subtitle_strings[i,j,k] = "Ok. Can you look up at the gaze point for me?";
    subtitle_cues_delta[i,j,k] = 2.5f;//13
    subtitle_pause_i_ice_0 = k;
    k++;
    subtitle_strings[i,j,k] = "Great! Now look at the one at your feet.";
    subtitle_cues_delta[i,j,k] = 2f;//15
    subtitle_pause_i_ice_1 = k;
    k++;
    subtitle_strings[i,j,k] = "Alright! Everything seems to be in order.";
    subtitle_cues_delta[i,j,k] = 2.25f;//16
    k++;
    subtitle_strings[i,j,k] = "Welcome to Ice Cube!";
    subtitle_cues_delta[i,j,k] = 2f;//18
    k++;
    subtitle_strings[i,j,k] = "I'm glad you could make it all the way";
    subtitle_cues_delta[i,j,k] = 1.5f;//20
    k++;
    subtitle_strings[i,j,k] = "down to antarctica for this mission.";
    subtitle_cues_delta[i,j,k] = 1.5f;//22
    k++;
    subtitle_strings[i,j,k] = "Before we send you off,";
    subtitle_cues_delta[i,j,k] = 2f;//24
    k++;
    subtitle_strings[i,j,k] = "let's brief you on why you're here:";
    subtitle_cues_delta[i,j,k] = 2f;//25
    k++;
    subtitle_strings[i,j,k] = "The Ice Cube Research Facility detects neutrino particles";
    subtitle_cues_delta[i,j,k] = 2.75f;//29
    k++;
    subtitle_strings[i,j,k] = "sent from deep out in space.";
    subtitle_cues_delta[i,j,k] = 1.2f;//30
    k++;
    subtitle_strings[i,j,k] = "I'll show you the sensors on your helmet overlay.";
    subtitle_cues_delta[i,j,k] = 2f;//32
    k++;
    subtitle_strings[i,j,k] = "See that grid below the facility?";
    subtitle_cues_delta[i,j,k] = 2f;//35
    k++;
    subtitle_strings[i,j,k] = "Each dot is a sensor that detects light";
    subtitle_cues_delta[i,j,k] = 3f;//38
    k++;
    subtitle_strings[i,j,k] = "from a passing neutrino particle.";
    subtitle_cues_delta[i,j,k] = 2f;//39
    k++;
    subtitle_strings[i,j,k] = "Look! It's just detected one now!";
    subtitle_cues_delta[i,j,k] = 3f;//42
    k++;
    subtitle_strings[i,j,k] = "This is great timing-";
    subtitle_cues_delta[i,j,k] = 2f;//44
    k++;
    subtitle_strings[i,j,k] = "we'll use the sensor data";
    subtitle_cues_delta[i,j,k] = 1f;//45
    k++;
    subtitle_strings[i,j,k] = "to pinpoint where this came from in outer space...";
    subtitle_cues_delta[i,j,k] = 2f;//47
    k++;
    subtitle_strings[i,j,k] = "annnnnd... done!";
    subtitle_cues_delta[i,j,k] = 2f;//49
    k++;
    subtitle_strings[i,j,k] = "Now your job is to follow this trajectory";
    subtitle_cues_delta[i,j,k] = 3f;//52
    k++;
    subtitle_strings[i,j,k] = "out into space to find the source";
    subtitle_cues_delta[i,j,k] = 2f;//54
    k++;
    subtitle_strings[i,j,k] = "You're going to use your suit's Impossible Drive";
    subtitle_cues_delta[i,j,k] = 3f;//57
    k++;
    subtitle_strings[i,j,k] = "and follow the path of the neutrino-";
    subtitle_cues_delta[i,j,k] = 2f;//59
    k++;
    subtitle_strings[i,j,k] = "All you have do to is";
    subtitle_cues_delta[i,j,k] = 1f;//60
    k++;
    subtitle_strings[i,j,k] = "look at the gaze point at the end of the path...";
    subtitle_cues_delta[i,j,k] = 2f;//62
    k++;
    subtitle_cues_delta[i,j,k] = 2f;
    k++;

    j = (int)SPEC.COUNT;
    k = 0;
    subtitle_cues_delta[i,j,k] = 1f;
    k++;

    //VOYAGER
    i = (int)SCENE.VOYAGER;
    j = (int)SPEC.VIZ;
    k = 0;
    subtitle_strings[i,j,k] = "";
    subtitle_cues_delta[i,j,k] = 1f;
    k++;
    subtitle_strings[i,j,k] = "Hello? ...";
    subtitle_cues_delta[i,j,k] = 0.5f;//1
    k++;
    subtitle_strings[i,j,k] = "You still there?";
    subtitle_cues_delta[i,j,k] = 0.5f;//1
    k++;
    subtitle_strings[i,j,k] = "Did you make it in one piece?";
    subtitle_cues_delta[i,j,k] = 1f;//2
    k++;
    subtitle_strings[i,j,k] = "Take a second to look around and find your bearings-";
    subtitle_cues_delta[i,j,k] = 3f;//5
    k++;
    subtitle_strings[i,j,k] = "it's probably pretty cool to be";
    subtitle_cues_delta[i,j,k] = 2f;//7
    k++;
    subtitle_strings[i,j,k] = "further out in space than any other human has ever been!";
    subtitle_cues_delta[i,j,k] = 2f;//9
    k++;
    subtitle_strings[i,j,k] = "Now you have a job to do.";
    subtitle_cues_delta[i,j,k] = 2f;//11
    k++;
    subtitle_strings[i,j,k] = "Follow the path of the neutrino";
    subtitle_cues_delta[i,j,k] = 2f;//13
    k++;
    subtitle_strings[i,j,k] = "that was detected by Ice Cube";
    subtitle_cues_delta[i,j,k] = 1f;//14
    k++;
    subtitle_strings[i,j,k] = "to discover the source.";
    subtitle_cues_delta[i,j,k] = 2f;//16
    k++;
    subtitle_strings[i,j,k] = "While we're waiting for the Impossible Drive to recharge,";
    subtitle_cues_delta[i,j,k] = 3f;//19
    k++;
    subtitle_strings[i,j,k] = "let's go over some other features of your suit.";
    subtitle_cues_delta[i,j,k] = 2f;//21
    k++;
    subtitle_strings[i,j,k] = "If you look at your feet,";
    subtitle_cues_delta[i,j,k] = 2f;//23
    k++;
    subtitle_strings[i,j,k] = "you can use the gaze points to";
    subtitle_cues_delta[i,j,k] = 1f;//24
    k++;
    subtitle_strings[i,j,k] = "switch out your helmet's view.";
    subtitle_cues_delta[i,j,k] = 2f;//26
    k++;
    subtitle_strings[i,j,k] = "Go ahead-";
    subtitle_cues_delta[i,j,k] = 1f;//27
    k++;
    subtitle_strings[i,j,k] = "look at your feet and switch to xray view.";
    subtitle_cues_delta[i,j,k] = 3f;//30
    k++;
    subtitle_cues_delta[i,j,k] = 1f;
    k++;

    j = (int)SPEC.GAM;
    k = 0;
    subtitle_strings[i,j,k] = "";
    subtitle_cues_delta[i,j,k] = 1f;
    k++;
    subtitle_strings[i,j,k] = "Pretty great, right?";
    subtitle_cues_delta[i,j,k] = 2f;//2
    k++;
    subtitle_strings[i,j,k] = "Look around- check out the galaxy!";
    subtitle_cues_delta[i,j,k] = 2f;//4
    k++;
    subtitle_strings[i,j,k] = "This is what the universe looks like when we see with xrays.";
    subtitle_cues_delta[i,j,k] = 4f;//8
    k++;
    subtitle_strings[i,j,k] = "Your helmet is detecting xrays";
    subtitle_cues_delta[i,j,k] = 2f;//10
    k++;
    subtitle_strings[i,j,k] = "in the same way your eye would normally detect light.";
    subtitle_cues_delta[i,j,k] = 2f;//12
    k++;
    subtitle_strings[i,j,k] = "Can you look at pluto for a second?";
    subtitle_cues_delta[i,j,k] = 2f;//14
    subtitle_pause_i_voyager_0 = k;
    k++;
    subtitle_strings[i,j,k] = "See how it's just a big black ball?";
    subtitle_cues_delta[i,j,k] = 2f;//16
    k++;
    subtitle_strings[i,j,k] = "That's because xrays don't pass through it.";
    subtitle_cues_delta[i,j,k] = 3f;//19
    k++;
    subtitle_strings[i,j,k] = "Now, let's switch to neutrino vision.";
    subtitle_cues_delta[i,j,k] = 2f;//21
    k++;
    subtitle_cues_delta[i,j,k] = 1f;
    k++;

    j = (int)SPEC.NEU;
    k = 0;
    subtitle_strings[i,j,k] = "";
    subtitle_cues_delta[i,j,k] = 1f;
    k++;
    subtitle_strings[i,j,k] = "Alright- look back to pluto:";
    subtitle_cues_delta[i,j,k] = 2f;//2
    subtitle_pause_i_voyager_1 = k;
    k++;
    subtitle_strings[i,j,k] = "where'd it go?!";
    subtitle_cues_delta[i,j,k] = 1f;//3
    k++;
    subtitle_strings[i,j,k] = "Pluto seems to have disappeared!";
    subtitle_cues_delta[i,j,k] = 1f;//4
    k++;
    subtitle_strings[i,j,k] = "Your helmet is now only sensing neutrino particles.";
    subtitle_cues_delta[i,j,k] = 3f;//7
    k++;
    subtitle_strings[i,j,k] = "Neutrinos pass through just about everything,";
    subtitle_cues_delta[i,j,k] = 3f;//10
    k++;
    subtitle_strings[i,j,k] = "Even whole planets!";
    subtitle_cues_delta[i,j,k] = 1f;//11
    k++;
    subtitle_strings[i,j,k] = "It's like pluto doesn't even exist to them!";
    subtitle_cues_delta[i,j,k] = 3f;//13
    k++;
    subtitle_strings[i,j,k] = "When you're ready,";
    subtitle_cues_delta[i,j,k] = 0.5f;//15
    k++;
    subtitle_strings[i,j,k] = "Look to the gaze point at the end of the neutrino path.";
    subtitle_cues_delta[i,j,k] = 2.5f;//18
    k++;
    subtitle_cues_delta[i,j,k] = 1f;
    k++;

    j = (int)SPEC.COUNT;
    k = 0;
    subtitle_strings[i,j,k] = "";
    subtitle_cues_delta[i,j,k] = 1f;
    k++;
    subtitle_cues_delta[i,j,k] = 1f;
    k++;

    //NOTHING
    i = (int)SCENE.NOTHING;
    j = (int)SPEC.VIZ;
    k = 0;
    subtitle_strings[i,j,k] = "";
    subtitle_cues_delta[i,j,k] = 1f;
    k++;
    subtitle_strings[i,j,k] = "We're getting some... pretty intense readings.";
    subtitle_cues_delta[i,j,k] = 2f;//2
    k++;
    subtitle_strings[i,j,k] = "You're... really far out in space.";
    subtitle_cues_delta[i,j,k] = 3f;//5
    k++;
    subtitle_strings[i,j,k] = "Ok- time to brief you with the details of your mission.";
    subtitle_cues_delta[i,j,k] = 3f;//8
    k++;
    subtitle_strings[i,j,k] = "As you've seen, we've given your suit the ability";
    subtitle_cues_delta[i,j,k] = 3f;//11
    k++;
    subtitle_strings[i,j,k] = "to see in three different ways:";
    subtitle_cues_delta[i,j,k] = 1f;//12
    k++;
    subtitle_strings[i,j,k] = "visible light, xray vision, and neutrino detection.";
    subtitle_cues_delta[i,j,k] = 4f;//16
    k++;
    subtitle_strings[i,j,k] = "The first two have been used for decades to look out into space.";
    subtitle_cues_delta[i,j,k] = 4f;//20
    k++;
    subtitle_strings[i,j,k] = "But if we want to see really far,";
    subtitle_cues_delta[i,j,k] = 2f;//22
    k++;
    subtitle_strings[i,j,k] = "Neutrinos are the only thing that will work.";
    subtitle_cues_delta[i,j,k] = 2f;//24
    k++;
    subtitle_strings[i,j,k] = "That's why we need the Ice Cube Observatory.";
    subtitle_cues_delta[i,j,k] = 3f;//27
    k++;
    subtitle_strings[i,j,k] = "The arrays of sensors in antartica";
    subtitle_cues_delta[i,j,k] = 3f;//30
    k++;
    subtitle_strings[i,j,k] = "allow us to detect neutrinos from deep space.";
    subtitle_cues_delta[i,j,k] = 2f;//32
    k++;
    subtitle_strings[i,j,k] = "That helps us map out parts of the universe";
    subtitle_cues_delta[i,j,k] = 3f;//35
    k++;
    subtitle_strings[i,j,k] = "invisible to other telescopes.";
    subtitle_cues_delta[i,j,k] = 1f;//36
    k++;
    subtitle_strings[i,j,k] = "The question you have to answer is:";
    subtitle_cues_delta[i,j,k] = 3f;//39
    k++;
    subtitle_strings[i,j,k] = "What sent the neutrino that";
    subtitle_cues_delta[i,j,k] = 2f;//41
    k++;
    subtitle_strings[i,j,k] = "Ice Cube detected back at earth?";
    subtitle_cues_delta[i,j,k] = 2f;//43
    k++;
    subtitle_strings[i,j,k] = "When you find the source at the end of your journey,";
    subtitle_cues_delta[i,j,k] = 3f;//46
    k++;
    subtitle_strings[i,j,k] = "you'll need to collect data from it";
    subtitle_cues_delta[i,j,k] = 1f;//47
    k++;
    subtitle_strings[i,j,k] = "using each of the three methods we've given you.";
    subtitle_cues_delta[i,j,k] = 2f;//49
    k++;
    subtitle_strings[i,j,k] = "You'll use your visible light,";
    subtitle_cues_delta[i,j,k] = 2f;//51
    k++;
    subtitle_strings[i,j,k] = "XRay, and Neutrino view to collect these readings.";
    subtitle_cues_delta[i,j,k] = 4f;//55
    k++;
    subtitle_strings[i,j,k] = "Ok. Things might get dicey going forward.";
    subtitle_cues_delta[i,j,k] = 4f;//59
    k++;
    subtitle_strings[i,j,k] = "Good luck.";
    subtitle_cues_delta[i,j,k] = 3f;//61
    k++;
    subtitle_cues_delta[i,j,k] = 1f;
    k++;

    j = (int)SPEC.COUNT;
    k = 0;
    subtitle_strings[i,j,k] = "";
    subtitle_cues_delta[i,j,k] = 1f;
    k++;
    subtitle_cues_delta[i,j,k] = 1f;
    k++;

    //EXTREME
    i = (int)SCENE.EXTREME;
    j = (int)SPEC.VIZ;
    k = 0;
    subtitle_strings[i,j,k] = "";
    subtitle_cues_delta[i,j,k] = 1f;
    k++;
    subtitle_strings[i,j,k] = "Hello?";
    subtitle_cues_delta[i,j,k] = 1f;
    k++;
    subtitle_strings[i,j,k] = "Do you read me?";
    subtitle_cues_delta[i,j,k] = 1f;
    k++;
    subtitle_strings[i,j,k] = "You've discovered a black hole!";
    subtitle_cues_delta[i,j,k] = 3f;
    k++;
    subtitle_strings[i,j,k] = "You need to scan it with each";
    subtitle_cues_delta[i,j,k] = 2f;
    k++;
    subtitle_strings[i,j,k] = "of your vision modules quickly!";
    subtitle_cues_delta[i,j,k] = 2f;
    k++;
    subtitle_strings[i,j,k] = "Then get OUT of there!";
    subtitle_cues_delta[i,j,k] = 3f;
    k++;
    subtitle_strings[i,j,k] = "Make sure you've selected visibile light vision-";
    subtitle_cues_delta[i,j,k] = 2f;
    k++;
    subtitle_strings[i,j,k] = "look at the black hole and collect visible light readings!";
    subtitle_cues_delta[i,j,k] = 4f;
    k++;
    subtitle_cues_delta[i,j,k] = 2f;
    k++;

    j = (int)SPEC.GAM;
    k = 0;
    subtitle_strings[i,j,k] = "";
    subtitle_cues_delta[i,j,k] = 1f;
    k++;
    subtitle_strings[i,j,k] = "Look up at the black hole, and collect the XRay readings!";
    subtitle_cues_delta[i,j,k] = 4f;
    k++;
    subtitle_cues_delta[i,j,k] = 2f;
    k++;

    j = (int)SPEC.NEU;
    k = 0;
    subtitle_strings[i,j,k] = "";
    subtitle_cues_delta[i,j,k] = 1f;
    k++;
    subtitle_strings[i,j,k] = "Look back at the black hole, and collect the neutrino readings!";
    subtitle_cues_delta[i,j,k] = 4f;
    k++;
    subtitle_cues_delta[i,j,k] = 1f;
    k++;

    j = (int)SPEC.COUNT;
    k = 0;
    subtitle_strings[i,j,k] = "";
    subtitle_cues_delta[i,j,k] = 1f;
    k++;
    subtitle_strings[i,j,k] = "You did it! We have the data!";
    subtitle_cues_delta[i,j,k] = 3f;
    k++;
    subtitle_strings[i,j,k] = "Now follow the neutrino path back to earth!";
    subtitle_cues_delta[i,j,k] = 3f;
    k++;
    subtitle_cues_delta[i,j,k] = 1f;
    k++;

    //EARTH
    j = (int)SPEC.VIZ;
    k = 0;
    subtitle_strings[i,j,k] = "";
    subtitle_cues_delta[i,j,k] = 1f;
    k++;
    subtitle_cues_delta[i,j,k] = 1f;
    k++;


    j = (int)SPEC.GAM;
    k = 0;
    subtitle_strings[i,j,k] = "";
    subtitle_cues_delta[i,j,k] = 1f;
    k++;
    subtitle_cues_delta[i,j,k] = 1f;
    k++;


    j = (int)SPEC.NEU;
    k = 0;
    subtitle_strings[i,j,k] = "";
    subtitle_cues_delta[i,j,k] = 1f;
    k++;
    subtitle_cues_delta[i,j,k] = 1f;
    k++;

    i = (int)SCENE.EARTH;
    j = (int)SPEC.COUNT;
    k = 0;
    subtitle_strings[i,j,k] = "";
    subtitle_cues_delta[i,j,k] = 1f;
    k++;
    subtitle_strings[i,j,k] = "Wow! You did it!";
    subtitle_cues_delta[i,j,k] = 2f;//2
    k++;
    subtitle_strings[i,j,k] = "I... can't believe you're alive!";
    subtitle_cues_delta[i,j,k] = 0.5f;//2
    k++;
    subtitle_strings[i,j,k] = "....I mean, congratulations, agent!";
    subtitle_cues_delta[i,j,k] = 1.5f;//4
    k++;
    subtitle_strings[i,j,k] = "It looks like the source";
    subtitle_cues_delta[i,j,k] = 2f;//6
    k++;
    subtitle_strings[i,j,k] = "of the neutrino particle we detected with Ice Cube";
    subtitle_cues_delta[i,j,k] = 3f;//9
    k++;
    subtitle_strings[i,j,k] = "was a black hole!";
    subtitle_cues_delta[i,j,k] = 1f;//10
    k++;
    subtitle_strings[i,j,k] = "Black holes are one of the strangest,";
    subtitle_cues_delta[i,j,k] = 2f;//12
    k++;
    subtitle_strings[i,j,k] = "most extreme objects in the whole universe!";
    subtitle_cues_delta[i,j,k] = 2f;//14
    k++;
    subtitle_strings[i,j,k] = "Did you know that black holes can have the mass";
    subtitle_cues_delta[i,j,k] = 3f;//17
    k++;
    subtitle_strings[i,j,k] = "of several million suns?";
    subtitle_cues_delta[i,j,k] = 2f;//19
    k++;
    subtitle_strings[i,j,k] = "One spoonful of black hole could weigh as much";
    subtitle_cues_delta[i,j,k] = 3f;//22
    k++;
    subtitle_strings[i,j,k] = "as a whole planet!";
    subtitle_cues_delta[i,j,k] = 1f;//23
    k++;
    subtitle_strings[i,j,k] = "They also emit high energy neutrinos";
    subtitle_cues_delta[i,j,k] = 3f;//26
    k++;
    subtitle_strings[i,j,k] = "that travel millions of lightyears back to earth.";
    subtitle_cues_delta[i,j,k] = 2f;//28
    k++;
    subtitle_strings[i,j,k] = "It would have gone totally unnoticed";
    subtitle_cues_delta[i,j,k] = 3f;//31
    k++;
    subtitle_strings[i,j,k] = "if it weren't for the scientists at Ice Cube.";
    subtitle_cues_delta[i,j,k] = 2f;//33
    k++;
    subtitle_strings[i,j,k] = "Black holes are very hard to detect-";
    subtitle_cues_delta[i,j,k] = 3f;//36
    k++;
    subtitle_strings[i,j,k] = "well- because they're black!";
    subtitle_cues_delta[i,j,k] = 1f;//37
    k++;
    subtitle_strings[i,j,k] = "It's impossible to see something black on a black background of space!";
    subtitle_cues_delta[i,j,k] = 4f;//41
    k++;
    subtitle_strings[i,j,k] = "Fortunately, ICE CUBE has found a way to observe them using neutrinos!";
    subtitle_cues_delta[i,j,k] = 4f;//45
    k++;
    subtitle_strings[i,j,k] = "Well, that's mission complete on our end.";
    subtitle_cues_delta[i,j,k] = 4f;//49
    k++;
    subtitle_strings[i,j,k] = "Until next time!";
    subtitle_cues_delta[i,j,k] = 1f;//50
    k++;
    subtitle_cues_delta[i,j,k] = 1f;
    k++;

    //CREDITS
    i = (int)SCENE.CREDITS;
    j = (int)SPEC.VIZ;
    k = 0;
    subtitle_strings[i,j,k] = "";
    subtitle_cues_delta[i,j,k] = 1f;
    k++;
    subtitle_cues_delta[i,j,k] = 1f;
    k++;

    j = (int)SPEC.COUNT;
    k = 0;
    subtitle_strings[i,j,k] = "";
    subtitle_cues_delta[i,j,k] = 1f;
    k++;
    subtitle_cues_delta[i,j,k] = 1f;
    k++;

    //gen absolutes from deltas
    for(i = 0; i < (int)SCENE.COUNT; i++)
    for(j = 0; j < (int)SPEC.COUNT+1; j++)
    for(k = 1; k < MAX_SUBTITLES_PER_CLIP; k++)
    {
      subtitle_cues_absolute[i,j,k] = subtitle_cues_absolute[i,j,k-1]+subtitle_cues_delta[i,j,k-1];
    }
    }

    music_files = new string[(int)SCENE.COUNT,(int)SPEC.COUNT];
    for(int i = 0; i < (int)SCENE.COUNT; i++)
    {
      for(int j = 0; j < (int)SPEC.COUNT; j++)
      {
        music_files[i,j] = "Music/"+spec_names[j]+"/"+scene_names[i];
      }
    }
    musics = new AudioClip[(int)SCENE.COUNT,(int)SPEC.COUNT];
    music_vols = new float[(int)SCENE.COUNT,(int)SPEC.COUNT];
    for(int i = 0; i < (int)SCENE.COUNT; i++)
    {
      for(int j = 0; j < (int)SPEC.COUNT; j++)
      {
        musics[i,j] = Resources.Load<AudioClip>(music_files[i,j]);
        music_vols[i,j] = 1.0f;
      }
    }

    n_sfx_audiosources = 5;
    sfx_audiosource_i = 0;
    sfx_audiosource = new AudioSource[n_sfx_audiosources];
    for(int i = 0; i < n_sfx_audiosources; i++)
    {
      sfx_audiosource[i] = GameObject.Find("Script").AddComponent<AudioSource>();
      sfx_audiosource[i].priority = 3;
    }
    sfxs = new AudioClip[(int)SFX.COUNT];
    sfx_vols = new float[(int)SFX.COUNT];
    for(int i = 0; i < (int)SFX.COUNT; i++)
    {
      sfxs[i] = Resources.Load<AudioClip>(sfx_files[i]);
      sfx_vols[i] = 1.0f;
    }

    string[,] skybox_files = new string[(int)SCENE.COUNT, (int)SPEC.COUNT];
    for(int i = 0; i < (int)SCENE.COUNT; i++)
      for(int j = 0; j < (int)SPEC.COUNT; j++)
        skybox_files[i,j] = "Skybox/"+spec_names[j]+"/"+scene_names[i]+"/"+scene_names[i];

    helmet_colors = new Color[(int)SCENE.COUNT];
    helmet_colors[0] = scene0_helmet_color;
    helmet_colors[1] = scene1_helmet_color;
    helmet_colors[2] = scene2_helmet_color;
    helmet_colors[3] = scene3_helmet_color;
    helmet_colors[4] = scene4_helmet_color;

    ta = new float[(int)SCENE.COUNT, (int)SPEC.COUNT];

    blackhole_spec_triggered = new int[(int)SPEC.COUNT];

    advance_trigger = new gaze_trigger();
    spec_trigger    = new gaze_trigger();
    warp_trigger    = new gaze_trigger();
    warp_trigger.t_max_numb = 4f;
    blackhole_trigger = new gaze_trigger();
    blackhole_trigger.range = 0.8f;

    default_layer = LayerMask.NameToLayer("Default");

    camera_house = GameObject.Find("CameraHouse");
    //main_camera = GameObject.Find("Main Camera");
    main_camera = GameObject.Find("CenterEyeAnchor");
    main_camera_skybox = main_camera.GetComponent<Skybox>();
    portal_projection = GameObject.Find("Portal_Projection");
    portal = GameObject.Find("Portal");
    portal_camera_next = GameObject.Find("Portal_Camera_Next");
    portal_camera_next_skybox = portal_camera_next.GetComponent<Skybox>();
    helmet = GameObject.Find("Helmet");
    helmet_light = GameObject.Find("Helmet_Light");
    helmet_light_light = helmet_light.GetComponent<Light>();
    cam_reticle = GameObject.Find("Cam_Reticle");
    cam_spinner = GameObject.Find("Cam_Spinner");
    reticle_d = cam_reticle.transform.position.z;
    gaze_projection = GameObject.Find("Gaze_Projection");
    gaze_reticle = GameObject.Find("Gaze_Reticle");
    spec_projection = GameObject.Find("Spec_Projection");
    spec_viz_reticle = GameObject.Find("Spec_Viz_Reticle");
    spec_gam_reticle = GameObject.Find("Spec_Gam_Reticle");
    spec_neu_reticle = GameObject.Find("Spec_Neu_Reticle");
    spec_sel_reticle = GameObject.Find("Spec_Sel_Reticle");
    gazeray = GameObject.Find("Ray");
    gazeball = GameObject.Find("Ball");
    grid = GameObject.Find("MyGrid");
    grid_oyoff = grid.transform.position.y;
    grid.transform.position = new Vector3(grid.transform.position.x,grid_oyoff+grid_yoff,grid.transform.position.z);
    ar_group = GameObject.Find("AR");
    ar_camera_project = GameObject.Find("AR_Camera_Project");
    ar_camera_static = GameObject.Find("AR_Camera_Static");
    ar_maps = new GameObject[3];
    ar_maps[0] = GameObject.Find("map0");
    ar_maps[1] = GameObject.Find("map1");
    ar_maps[2] = GameObject.Find("map2");
    ar_alert = GameObject.Find("Alert");
    ar_timer = GameObject.Find("Timer");
    ar_timer_text = ar_timer.GetComponent<TextMesh>();
    credits_0 = GameObject.Find("Credits_0");
    credits_text_0 = credits_0.GetComponent<TextMesh>();
    credits_1 = GameObject.Find("Credits_1");
    credits_text_1 = credits_1.GetComponent<TextMesh>();
    subtitles = GameObject.Find("Subtitles");
    subtitles_text = subtitles.GetComponent<TextMesh>();
    //stars = GameObject.Find("Stars");
    //starsscale = GameObject.Find("StarsScale");

    ar_label_lefts      = new GameObject[MAX_LABELS];
    ar_label_left_kids  = new GameObject[MAX_LABELS];
    ar_label_left_quads = new GameObject[MAX_LABELS];
    ar_label_left_texts = new TextMesh[MAX_LABELS];
    ar_label_rights      = new GameObject[MAX_LABELS];
    ar_label_right_kids  = new GameObject[MAX_LABELS];
    ar_label_right_quads = new GameObject[MAX_LABELS];
    ar_label_right_texts = new TextMesh[MAX_LABELS];
    ar_label_bhs      = new GameObject[MAX_LABELS];
    ar_label_bh_kids  = new GameObject[MAX_LABELS];
    ar_label_bh_quads = new GameObject[MAX_LABELS];
    ar_label_bh_texts = new TextMesh[MAX_LABELS];

    //technically isn't connected to max labels, but as there's a label per line, it's at least upper bound
    ar_label_checks     = new GameObject[MAX_LABELS];
    ar_spec_checks      = new GameObject[MAX_LABELS];
    ar_progresses       = new GameObject[MAX_LABELS];
    ar_progress_offsets = new GameObject[MAX_LABELS];
    ar_progress_lines   = new LineRenderer[MAX_LABELS];

    float lw;
    AnimationCurve curve;
    lw = 0.0001f;
    curve = new AnimationCurve();
    curve.AddKey(0, lw);
    curve.AddKey(1, lw);
    for(int i = 0; i < MAX_LABELS; i++)
    {
      ar_label_lefts[i] = (GameObject)Instantiate(ar_label_left_prefab);
      ar_label_lefts[i].transform.parent = ar_group.transform;
      int k;
      k = 0;
      foreach(Transform child_transform in ar_label_lefts[i].transform)
      {
        GameObject child = child_transform.gameObject;
        switch(k)
        {
          case 0: ar_label_left_kids[i] = child; break;
        }
        k++;
      }
      k = 0;
      foreach(Transform child_transform in ar_label_left_kids[i].transform)
      {
        GameObject child = child_transform.gameObject;
        switch(k)
        {
          case 0: ar_label_left_quads[i] = child; break;
          case 1: ar_label_left_texts[i] = child.GetComponent<TextMesh>(); break;
        }
        k++;
      }

      ar_label_rights[i] = (GameObject)Instantiate(ar_label_right_prefab);
      ar_label_rights[i].transform.parent = ar_group.transform;
      k = 0;
      foreach(Transform child_transform in ar_label_rights[i].transform)
      {
        GameObject child = child_transform.gameObject;
        switch(k)
        {
          case 0: ar_label_right_kids[i] = child; break;
        }
        k++;
      }
      k = 0;
      foreach(Transform child_transform in ar_label_right_kids[i].transform)
      {
        GameObject child = child_transform.gameObject;
        switch(k)
        {
          case 0: ar_label_right_quads[i] = child; break;
          case 1: ar_label_right_texts[i] = child.GetComponent<TextMesh>(); break;
        }
        k++;
      }

      ar_label_bhs[i] = (GameObject)Instantiate(ar_label_bh_prefab);
      ar_label_bhs[i].transform.parent = ar_group.transform;
      k = 0;
      foreach(Transform child_transform in ar_label_bhs[i].transform)
      {
        GameObject child = child_transform.gameObject;
        switch(k)
        {
          case 0: ar_label_bh_kids[i] = child; break;
        }
        k++;
      }
      k = 0;
      foreach(Transform child_transform in ar_label_bh_kids[i].transform)
      {
        GameObject child = child_transform.gameObject;
        switch(k)
        {
          case 0: ar_label_bh_quads[i] = child; break;
          case 1: ar_label_bh_texts[i] = child.GetComponent<TextMesh>(); break;
        }
        k++;
      }

      ar_label_checks[i] = (GameObject)Instantiate(ar_check_prefab);
      ar_label_checks[i].transform.parent = ar_label_bh_kids[i].transform;
      ar_spec_checks[i] = (GameObject)Instantiate(ar_check_prefab); //just to ensure full instantiate- actually set details below (unrelated to labels)

      ar_progress_offsets[i] = (GameObject)Instantiate(ar_progress_prefab);
      ar_progress_offsets[i].transform.parent = ar_group.transform;
      ar_progresses[i] = ar_progress_offsets[i].transform.GetChild(0).gameObject;
      ar_progress_lines[i] = ar_progresses[i].GetComponent<LineRenderer>();

      ar_progress_lines[i].widthCurve = curve;
      for(int j = 0; j < 2; j++)
        ar_progress_lines[i].SetPosition(j, new Vector3(0, 0, 0));
    }

    ar_spec_checks[0].transform.parent = spec_viz_reticle.transform;
    ar_spec_checks[1].transform.parent = spec_gam_reticle.transform;
    ar_spec_checks[2].transform.parent = spec_neu_reticle.transform;
    for(int i = 0; i < 3; i++)
    {
      ar_spec_checks[i].transform.localPosition = new Vector3(0.0f,-1.0f,0.0f);
      ar_spec_checks[i].transform.localScale = new Vector3(1.0f,1.0f,1.0f);
    }

    ar_alert.SetActive(false);
    ar_timer.SetActive(false);

    icecube   = new GameObject[(int)SPEC.COUNT];
    pluto     = new GameObject[(int)SPEC.COUNT];
    vearth    = new GameObject[(int)SPEC.COUNT];
    milky     = new GameObject[(int)SPEC.COUNT];
    nearth    = new GameObject[(int)SPEC.COUNT];
    blackhole = new GameObject[(int)SPEC.COUNT];
    esun      = new GameObject[(int)SPEC.COUNT];
    earth     = new GameObject[(int)SPEC.COUNT];
    for(int i = 0; i < (int)SPEC.COUNT; i++)
    {
      icecube[i]   = GameObject.Find("Icecube_"   + spec_names[i]);
      pluto[i]     = GameObject.Find("Pluto_"     + spec_names[i]);
      vearth[i]    = GameObject.Find("VEarth_"    + spec_names[i]);
      milky[i]     = GameObject.Find("Milky_"     + spec_names[i]);
      nearth[i]    = GameObject.Find("NEarth_"    + spec_names[i]);
      blackhole[i] = GameObject.Find("BlackHole_" + spec_names[i]);
      esun[i]      = GameObject.Find("ESun_"      + spec_names[i]);
      earth[i]     = GameObject.Find("Earth_"     + spec_names[i]);
    }

    alpha_id = Shader.PropertyToID("alpha");
    time_mod_twelve_pi_id = Shader.PropertyToID("time_mod_twelve_pi");
    jitter_id = Shader.PropertyToID("jitter");

    voiceover_audiosource = GameObject.Find("Script").AddComponent<AudioSource>();
    voiceover_audiosource.priority = 1;
    voiceover_was_playing = false;
    music_audiosource = GameObject.Find("Script").AddComponent<AudioSource>();
    voiceover_audiosource.priority = 2;
    music_was_playing = false;

    default_portal_scale = portal.transform.localScale;
    default_portal_position = portal.transform.position;

    default_look_ahead = new Vector3(0, 0, 1);
    look_ahead = default_look_ahead;
    lazy_look_ahead = default_look_ahead;
    very_lazy_look_ahead = default_look_ahead;
    player_head = new Vector3(0, 2, 0);

    camera_house.transform.rotation = Quaternion.Euler((mouse_y - Screen.height / 2) * -2, (mouse_x - Screen.width / 2) * 2, 0);

    gaze_pt = new Vector3(1f, .8f, -1f).normalized;

    gaze_pt *= 1000;
    cam_euler = getCamEuler(cam_reticle.transform.position);
    gaze_cam_euler = getCamEuler(gaze_pt);
    anti_gaze_pt = new Vector3(-330f, -350f, 575f);
    anti_gaze_cam_euler = getCamEuler(anti_gaze_pt);

    gazeray.GetComponent<LineRenderer>().SetPosition(0, anti_gaze_pt);
    gazeray.GetComponent<LineRenderer>().SetPosition(1, gaze_pt);
    gazeball.transform.position = gaze_pt;

    for(int i = 0; i < (int)SPEC.COUNT; i++)
    {
      vearth[i].transform.position = anti_gaze_pt;
      nearth[i].transform.position = anti_gaze_pt;
    }
    earth[0].transform.position = anti_gaze_pt.normalized*600+new Vector3(0.0f,0.0f,0.0f);

    scene_centers[(int)SCENE.ICE]     = icecube[  0].transform.position;
    scene_centers[(int)SCENE.VOYAGER] = pluto[    0].transform.position;
    scene_centers[(int)SCENE.NOTHING] = milky[    0].transform.position;
    scene_centers[(int)SCENE.EXTREME] = blackhole[0].transform.position;
    scene_centers[(int)SCENE.EARTH]   = earth[    0].transform.position;
    scene_centers[(int)SCENE.CREDITS] = new Vector3(0,0,0);

    spec_euler = cam_euler;
    spec_euler.x = -3.141592f / 3f;
    spec_projection.transform.rotation = rotationFromEuler(spec_euler);

    skyboxes = new Material[(int)SCENE.COUNT, (int)SPEC.COUNT];
    for(int i = 0; i < (int)SCENE.COUNT; i++)
      for(int j = 0; j < (int)SPEC.COUNT; j++)
        skyboxes[i,j] = Resources.Load<Material>(skybox_files[i,j]);
    main_camera_skybox.material = skyboxes[cur_scene_i, cur_spec_i];
    portal_camera_next_skybox.material = skyboxes[next_scene_i, (int)SPEC.VIZ];

    //grid
    //GameObject grid_string;
    GameObject grid_bulb;
    //kill placement cube
    GameObject c = grid.transform.GetChild(0).gameObject;
    c.transform.parent = null;
    Destroy(c);

    grid_s = new float[grid_w,grid_h,grid_d];
    grid_bulbs = new GameObject[grid_w,grid_h,grid_d];

    for(int i = 0; i < grid_w; i++)
    {
      for(int j = 0; j < grid_d; j++)
      {
        float x = -0.5f+((float)i/(grid_w-1f));
        float z = -0.5f+((float)j/(grid_d-1f));
        /*
        grid_string = (GameObject)Instantiate(grid_string_prefab);
        grid_string.transform.SetParent(grid.transform);
        grid_string.transform.localPosition = new Vector3(x,0,z);
        grid_string.transform.localScale = grid_string.transform.localScale*grid.transform.localScale.x; //unity...
        */

        for(int k = 0; k < grid_h; k++)
        {
          float y = -0.5f+((float)k/(grid_h-1f));
          grid_bulb = (GameObject)Instantiate(grid_bulb_prefab);
          grid_bulb.transform.SetParent(grid.transform);
          grid_bulb.transform.localPosition = new Vector3(x,y,z);
          grid_bulb.transform.localScale = grid_bulb.transform.localScale*grid.transform.localScale.x; //unity...
          default_grid_s = grid_bulb.transform.localScale.x;
          grid_s[i,k,j] = 1;
          grid_bulbs[i,k,j] = grid_bulb;
        }
      }
    }
    grid.SetActive(false);

/*
    //stars
    GameObject[] star_groups;
    GameObject star;
    Vector3[] star_positions;
    Vector3 starpos;

    int n_stars = 0;//100000;
    int n_groups = (int)Mathf.Ceil(n_stars / 1000);
    int n_stars_in_group;
    star_groups = new GameObject[n_groups];
    star = (GameObject)Instantiate(star_prefab);

    //gen positions
    star_positions = new Vector3[n_stars];
    for(int i = 0; i < n_stars; i++)
    {
      bool good_star = false;
      starpos = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f));
      good_star = (starpos.sqrMagnitude < 1f);
      while(!good_star)
      {
        starpos = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f));
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
        Vector3 delta = new Vector3(0, 0, 0);
        Vector3 dist;
        for(int j = 0; j < n_stars; j++)
        {
          if(j != i)
          {
            dist = star_positions[j] - star_positions[i];
            if(dist.sqrMagnitude < 0.1)
            {
              delta += dist * (0.0001f / dist.sqrMagnitude);
            }
          }
        }
        star_positions[i] = (star_positions[i] + delta).normalized;
      }
    }

    //gen assets
    for(int i = 0; i < n_groups; i++)
    {
      n_stars_in_group = Mathf.Min(1000, n_stars);
      CombineInstance[] combine = new CombineInstance[n_stars_in_group];

      for(int j = 0; j < n_stars_in_group; j++)
      {
        starpos = star_positions[i * n_stars_in_group + j];
        starpos *= Mathf.Lerp(0f, 30f, Random.Range(0f, 1f));

        star.transform.position = starpos;
        star.transform.rotation = Quaternion.Euler(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
        float ss = 0.1f;
        star.transform.localScale = new Vector3(ss, ss, ss);

        combine[j].mesh = star.transform.GetComponent<MeshFilter>().mesh;
        combine[j].transform = star.transform.localToWorldMatrix;
      }

      star_groups[i] = (GameObject)Instantiate(star_prefab);
      star_groups[i].transform.parent = starsscale.transform;
      //star_groups[i].layer = stars_layer;
      star_groups[i].transform.localPosition = new Vector3(0, 0, 0);
      star_groups[i].transform.localRotation = Quaternion.Euler(0, 0, 0);
      star_groups[i].transform.localScale = new Vector3(1, 1, 1);
      star_groups[i].transform.GetComponent<MeshFilter>().mesh = new Mesh();
      star_groups[i].transform.GetComponent<MeshFilter>().mesh.CombineMeshes(combine);

      n_stars -= n_stars_in_group;
    }
    Destroy(star, 0f);
*/

    reStart();
    MapVols();
    SetupScene();
  }

  //called just before portal to next scene appears
  void PreSetupNextScene()
  {
    scene_rots[next_scene_i] = 0;
    switch(next_scene_i)
    {

      case (int)SCENE.ICE:
        break;

      case (int)SCENE.VOYAGER:
        break;

      case (int)SCENE.NOTHING:
        break;

      case (int)SCENE.EXTREME:
        for(int i = 0; i < (int)SPEC.COUNT; i++)
        {
          foreach(Transform child_transform in blackhole[i].transform)
          {
            GameObject child = child_transform.gameObject;
            ParticleSystem ps = child.GetComponent<ParticleSystem>();
            if(ps) ps.Play();
          }
        }
        break;

      case (int)SCENE.EARTH:
        break;

      case (int)SCENE.CREDITS:
        break;

    }
  }

  void SetupScene()
  {
    //leaving actualy gameanalytics in (but commented out) so it's easy to find/replace them
    if(cur_scene_i == (int)SCENE.ICE)
    {
      //GameAnalytics.NewProgressionEvent(GAProgressionStatus.Start, "Universe", "Scene_" + cur_scene_i, "viz", 0);
    }
    else
    {
      //int last_scene_i = cur_scene_i - 1;
      //GameAnalytics.NewProgressionEvent(GAProgressionStatus.Complete, "Universe", "Scene_" + last_scene_i, "viz", 0);
      //GameAnalytics.NewProgressionEvent(GAProgressionStatus.Start,    "Universe", "Scene_" + cur_scene_i,  "viz", 0);
    }

    SetSpec((int)SPEC.VIZ);

    for(int i = 0; i < 3; i++)
      ar_maps[i].SetActive(false);
    spec_projection.SetActive(false);
    gaze_reticle.SetActive(false);

    main_camera_skybox.material = skyboxes[cur_scene_i, cur_spec_i];

    if(cur_scene_i != (int)SCENE.ICE)
    {
      gazeray.SetActive(true);
      gazeball.SetActive(true);
    }

    AnimationCurve curve;
    float lw;

    lw = 0.0001f;
    curve = new AnimationCurve();
    curve.AddKey(0, lw);
    curve.AddKey(1, lw);
    for(int i = 0; i < MAX_LABELS; i++)
    {
      ar_label_left_texts[i].text = "";
      ar_label_right_texts[i].text = "";
      ar_label_bh_texts[i].text = "";

      ar_label_lefts[ i].transform.localScale = new Vector3(0f,0f,0f);
      ar_label_rights[i].transform.localScale = new Vector3(0f,0f,0f);
      ar_label_bhs[   i].transform.localScale = new Vector3(0f,0f,0f);
      ar_label_lefts[ i].transform.position = new Vector3(0f,0f,0f);
      ar_label_rights[i].transform.position = new Vector3(0f,0f,0f);
      ar_label_bhs[   i].transform.position = new Vector3(0f,0f,0f);
      ar_label_left_kids[ i].transform.localPosition = new Vector3(0f,0f,0f);
      ar_label_right_kids[i].transform.localPosition = new Vector3(0f,0f,0f);
      ar_label_bh_kids[   i].transform.localPosition = new Vector3(0f,0f,0f);
      ar_label_checks[i].SetActive(false);
      ar_spec_checks[i].SetActive(false);

      ar_progress_lines[i].widthCurve = curve;
      for(int j = 0; j < 2; j++)
        ar_progress_lines[i].SetPosition(j, new Vector3(0, 0, 0));
    }

    int label_left_i = 0;
    int label_right_i = 0;
    int label_bh_i = 0;
    switch(cur_scene_i)
    {

      case (int)SCENE.ICE:
        advance_passed_ice_0 = false;
        advance_passed_ice_1 = false;

        ar_label_rights[label_right_i].transform.localScale = new Vector3(3f, 3f, 3f);
        ar_label_rights[    label_right_i].transform.position = icecube[0].transform.position;
        ar_label_right_kids[label_right_i].transform.localPosition = new Vector3(20,0,-20);
        ar_label_rights[label_right_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_rights[label_right_i].transform.position));
        ar_label_right_texts[label_right_i].text = "ICE CUBE";
        label_right_i++;

        gaze_projection.transform.rotation = rotationFromEuler(gaze_cam_euler);
        portal_projection.transform.rotation = rotationFromEuler(gaze_cam_euler);

        gazeray.SetActive(false);
        gazeball.SetActive(false);

        break;

      case (int)SCENE.VOYAGER:
        advance_passed_voyager_0 = false;
        advance_passed_voyager_1 = false;

        ar_label_rights[label_right_i].transform.localScale = new Vector3(10f, 10f, 10f);
        ar_label_rights[    label_right_i].transform.position = pluto[0].transform.position;
        ar_label_right_kids[label_right_i].transform.localPosition = new Vector3(11,0,0);
        ar_label_rights[label_right_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_rights[label_right_i].transform.position));
        ar_label_right_texts[label_right_i].text = "PLUTO";
        label_right_i++;

        ar_label_lefts[label_left_i].transform.localScale = new Vector3(10f, 10f, 10f);
        ar_label_lefts[    label_left_i].transform.position = vearth[0].transform.position;
        ar_label_left_kids[label_left_i].transform.localPosition = new Vector3(-5,0,0);
        ar_label_lefts[label_left_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_lefts[label_left_i].transform.position));
        ar_label_left_texts[label_left_i].text = "EARTH";
        label_left_i++;

        gaze_projection.transform.rotation = rotationFromEuler(gaze_cam_euler);
        portal_projection.transform.rotation = rotationFromEuler(gaze_cam_euler);

        ar_maps[0].SetActive(true);
        grid.SetActive(false);
        break;

      case (int)SCENE.NOTHING:

        ar_label_rights[label_right_i].transform.localScale = new Vector3(2f, 2f, 2f);
        ar_label_rights[    label_right_i].transform.position = milky[0].transform.position;
        ar_label_right_kids[label_right_i].transform.localPosition = new Vector3(10,-5,0);
        ar_label_rights[label_right_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_rights[label_right_i].transform.position));
        ar_label_right_texts[label_right_i].text = "MILKY WAY";
        label_right_i++;

        ar_label_lefts[label_left_i].transform.localScale = new Vector3(10f, 10f, 10f);
        ar_label_lefts[    label_left_i].transform.position = nearth[0].transform.position;
        ar_label_left_kids[label_left_i].transform.localPosition = new Vector3(-5,0,0);
        ar_label_lefts[label_left_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_lefts[label_left_i].transform.position));
        ar_label_left_texts[label_left_i].text = "EARTH";
        label_left_i++;

        gaze_projection.transform.rotation = rotationFromEuler(gaze_cam_euler);
        portal_projection.transform.rotation = rotationFromEuler(gaze_cam_euler);

        ar_maps[1].SetActive(true);
        spec_projection.SetActive(true);

        break;

      case (int)SCENE.EXTREME:

        float bar_y = -2;
        float bar_x = -11;

        ar_label_bhs[label_bh_i].transform.localScale = new Vector3(20f, 20f, 20f);
        ar_label_bhs[    label_bh_i].transform.position = blackhole[0].transform.position;
        ar_label_bh_kids[label_bh_i].transform.localPosition = new Vector3(25,5,0);
        ar_label_bhs[label_bh_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_bhs[label_bh_i].transform.position));
        ar_label_bh_texts[label_bh_i].text = "VISIBLE";

        ar_progress_offsets[label_bh_i].transform.localScale = ar_label_bhs[label_bh_i].transform.localScale;
        ar_progress_offsets[label_bh_i].transform.position   = ar_label_bh_kids[label_bh_i].transform.position;
        ar_progress_offsets[label_bh_i].transform.rotation   = ar_label_bhs[label_bh_i].transform.rotation;
        lw = 10f;
        curve = new AnimationCurve();
        curve.AddKey(0, lw);
        curve.AddKey(1, lw);
        ar_progress_lines[label_bh_i].widthCurve = curve;
        ar_progress_lines[label_bh_i].SetPosition(0, new Vector3(bar_x, bar_y, 0));
        ar_progress_lines[label_bh_i].SetPosition(1, new Vector3(bar_x, bar_y, 0));
        label_bh_i++;


        ar_label_bhs[label_bh_i].transform.localScale = new Vector3(20f, 20f, 20f);
        ar_label_bhs[    label_bh_i].transform.position = blackhole[0].transform.position;
        ar_label_bh_kids[label_bh_i].transform.localPosition = new Vector3(27,0,0);
        ar_label_bhs[label_bh_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_bhs[label_bh_i].transform.position));
        ar_label_bh_texts[label_bh_i].text = "XRAY";

        ar_progress_offsets[label_bh_i].transform.localScale = ar_label_bhs[label_bh_i].transform.localScale;
        ar_progress_offsets[label_bh_i].transform.position   = ar_label_bh_kids[label_bh_i].transform.position;
        ar_progress_offsets[label_bh_i].transform.rotation   = ar_label_bhs[label_bh_i].transform.rotation;
        lw = 10f;
        curve = new AnimationCurve();
        curve.AddKey(0, lw);
        curve.AddKey(1, lw);
        ar_progress_lines[label_bh_i].widthCurve = curve;
        ar_progress_lines[label_bh_i].SetPosition(0, new Vector3(bar_x, bar_y, 0));
        ar_progress_lines[label_bh_i].SetPosition(1, new Vector3(bar_x, bar_y, 0));
        label_bh_i++;


        ar_label_bhs[label_bh_i].transform.localScale = new Vector3(20f, 20f, 20f);
        ar_label_bhs[    label_bh_i].transform.position = blackhole[0].transform.position;
        ar_label_bh_kids[label_bh_i].transform.localPosition = new Vector3(25,-5,0);
        ar_label_bhs[label_bh_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_bhs[label_bh_i].transform.position));
        ar_label_bh_texts[label_bh_i].text = "NEUTRINO";

        ar_progress_offsets[label_bh_i].transform.localScale = ar_label_bhs[label_bh_i].transform.localScale;
        ar_progress_offsets[label_bh_i].transform.position   = ar_label_bh_kids[label_bh_i].transform.position;
        ar_progress_offsets[label_bh_i].transform.rotation   = ar_label_bhs[label_bh_i].transform.rotation;
        lw = 10f;
        curve = new AnimationCurve();
        curve.AddKey(0, lw);
        curve.AddKey(1, lw);
        ar_progress_lines[label_bh_i].widthCurve = curve;
        ar_progress_lines[label_bh_i].SetPosition(0, new Vector3(bar_x, bar_y, 0));
        ar_progress_lines[label_bh_i].SetPosition(1, new Vector3(bar_x, bar_y, 0));
        label_bh_i++;


        ar_label_lefts[label_left_i].transform.localScale = new Vector3(20f, 20f, 20f);
        ar_label_lefts[    label_left_i].transform.position = blackhole[0].transform.position;
        ar_label_left_kids[label_left_i].transform.localPosition = new Vector3(-25,0,0);
        ar_label_lefts[label_left_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_lefts[label_left_i].transform.position));
        ar_label_left_texts[label_left_i].text = "BLACK HOLE";
        label_left_i++;

        for(int j = 0; j < (int)SPEC.COUNT; j++)
          ta[(int)SCENE.EXTREME,j] = 0;

        //should have also been done in pre-setup, but can't hurt (in case of debug "start here")
        for(int i = 0; i < (int)SPEC.COUNT; i++)
        {
          foreach(Transform child_transform in blackhole[i].transform)
          {
            GameObject child = child_transform.gameObject;
            ParticleSystem ps = child.GetComponent<ParticleSystem>();
            if(ps) ps.Play();
          }
        }

        ar_maps[2].SetActive(true);
        spec_projection.SetActive(true);

        gaze_projection.transform.rotation = rotationFromEuler(anti_gaze_cam_euler);
        portal_projection.transform.rotation = rotationFromEuler(anti_gaze_cam_euler);

        ar_alert.SetActive(true);
        ar_timer.SetActive(true);
        timer_t = 0;
        alert_t = 0;

        break;

      case (int)SCENE.EARTH:

        ar_label_lefts[label_left_i].transform.localScale = new Vector3(8f, 8f, 8f);
        ar_label_lefts[    label_left_i].transform.position = earth[0].transform.position;
        ar_label_left_kids[label_left_i].transform.localPosition = new Vector3(-5,0,0);
        ar_label_lefts[label_left_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_lefts[label_left_i].transform.position));
        ar_label_left_texts[label_left_i].text = "ICE CUBE";
        label_left_i++;

        ar_label_rights[label_right_i].transform.localScale = new Vector3(8f, 8f, 8f);
        ar_label_rights[    label_right_i].transform.position = esun[0].transform.position;
        ar_label_right_kids[label_right_i].transform.localPosition = new Vector3(15,0,0);
        ar_label_rights[label_right_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_rights[label_right_i].transform.position));
        ar_label_right_texts[label_right_i].text = "SUN";
        label_right_i++;

        ar_alert.SetActive(false);
        ar_timer.SetActive(false);

        for(int i = 0; i < (int)SPEC.COUNT; i++)
        {
          foreach(Transform child_transform in blackhole[i].transform)
          {
            GameObject child = child_transform.gameObject;
            ParticleSystem ps = child.GetComponent<ParticleSystem>();
            if(ps) ps.Stop();
          }
        }

        gaze_projection.transform.rotation = rotationFromEuler(anti_gaze_cam_euler);
        portal_projection.transform.rotation = rotationFromEuler(anti_gaze_cam_euler);

        break;

      case (int)SCENE.CREDITS:

        gaze_reticle.SetActive(false);
        gazeray.SetActive(false);
        gazeball.SetActive(false);

        gaze_projection.transform.rotation = rotationFromEuler(gaze_cam_euler);
        portal_projection.transform.rotation = rotationFromEuler(gaze_cam_euler);

        break;

    }

    helmet_light_light.color = helmet_colors[cur_scene_i];

    //scene switched
    if(!voiceovers_played[cur_scene_i,(int)SPEC.VIZ] && dumb_delay_t > dumb_delay_t_max)
    {
      if(voiceover_audiosource.isPlaying) voiceover_audiosource.Stop();
      voiceover_audiosource.clip = voiceovers[cur_scene_i,(int)SPEC.VIZ];
      voiceover_audiosource.volume = voiceover_vols[cur_scene_i,(int)SPEC.VIZ];
      voiceover_audiosource.Play();
      voiceover_was_playing = true;
      voiceovers_played[cur_scene_i,(int)SPEC.VIZ] = true;
      subtitle_i = 0;
      subtitle_t = 0;
      subtitles_text.text = "";
      subtitle_spec = (int)SPEC.VIZ;
    }
    if(music_audiosource.isPlaying) music_audiosource.Stop();
    music_audiosource.clip = musics[cur_scene_i,(int)SPEC.VIZ];
    music_audiosource.volume = music_vols[cur_scene_i,(int)SPEC.VIZ];
    music_audiosource.Play();
    music_was_playing = true;
  }

  void UpdateScene()
  {
    float old_ta = ta[cur_scene_i,cur_spec_i];
    if(!advance_paused && hmd_mounted) ta[cur_scene_i,cur_spec_i] += Time.deltaTime;
    float cur_ta = ta[cur_scene_i,cur_spec_i];

    switch(cur_scene_i)
    {

      case (int)SCENE.ICE:

        float grid_t = 32f+dumb_delay_t_max;
        float pulse_t = 42f+dumb_delay_t_max;
        float beam_t = 49f+dumb_delay_t_max;

        //pulse
        if(cur_ta < pulse_t) nwave_t_10 = 0;

        //grid
        if(cur_ta >= grid_t)
        {
          if(old_ta < grid_t) //newly here
          {
            grid.SetActive(true);
            grid_yacc = 0.5f;
          }
          if(grid_yacc != 0)
          {
            grid_yvel += grid_yacc;
            grid_yoff += grid_yvel;
            if(grid_yoff > 0) { grid_yoff *= -1f; grid_yvel *= -0.5f; }
            grid_yvel *= 0.96f;
            if(Mathf.Abs(grid_yoff) < 1 && Mathf.Abs(grid_yvel) < 1) { grid_yoff = 0; grid_yvel = 0; grid_yacc = 0; }
            grid.transform.position = new Vector3(grid.transform.position.x,grid_oyoff+grid_yoff,grid.transform.position.z);
          }
        }

        //ray
        if(cur_ta >= beam_t)
          if(old_ta < beam_t) //newly here
          {
            gazeray.SetActive(true);
            gazeball.SetActive(true);
          }

        //command
        if(subtitle_i == subtitle_pause_i_ice_0 && !advance_passed_ice_0)
        {
          gaze_projection.transform.rotation = rotationFromEuler(getEuler(new Vector3(0f,10f,10f).normalized));
          gaze_reticle.SetActive(true);
          advance_trigger.position = gaze_reticle.transform.position;
          if(advance_trigger.tick(cam_reticle.transform.position,Time.deltaTime))
          {
            if(advance_trigger.just_triggered)
            {
              advance_passed_ice_0 = true;
              gaze_reticle.SetActive(false);
              gaze_projection.transform.rotation = rotationFromEuler(gaze_cam_euler);
            }
          }
        }
        if(subtitle_i == subtitle_pause_i_ice_1 && !advance_passed_ice_1)
        {
          gaze_projection.transform.rotation = rotationFromEuler(getEuler(new Vector3(0f,-20f,10f).normalized));
          gaze_reticle.SetActive(true);
          advance_trigger.position = gaze_reticle.transform.position;
          if(advance_trigger.tick(cam_reticle.transform.position,Time.deltaTime))
          {
            if(advance_trigger.just_triggered)
            {
              advance_passed_ice_1 = true;
              gaze_reticle.SetActive(false);
              gaze_projection.transform.rotation = rotationFromEuler(gaze_cam_euler);
            }
          }
        }

        break;

      case (int)SCENE.VOYAGER:

        /*
        float spec_t = 5f;

        //use this for timed enable
        if(cur_ta >= spec_t)
        {
          if(old_ta < spec_t) //newly here
          {
            spec_projection.SetActive(true);
          }
        }
        */
        if(!spec_projection.activeSelf && voiceovers_played[cur_scene_i,(int)SPEC.VIZ] && !voiceover_was_playing)
          spec_projection.SetActive(true);

        //command
        if(cur_spec_i == (int)SPEC.GAM && subtitle_i == subtitle_pause_i_voyager_0 && !advance_passed_voyager_0)
        {
          gaze_projection.transform.rotation = rotationFromEuler(getCamEuler(pluto[0].transform.position));
          gaze_reticle.SetActive(true);
          advance_trigger.position = gaze_reticle.transform.position;
          if(advance_trigger.tick(cam_reticle.transform.position,Time.deltaTime))
          {
            if(advance_trigger.just_triggered)
            {
              advance_passed_voyager_0 = true;
              gaze_reticle.SetActive(false);
              gaze_projection.transform.rotation = rotationFromEuler(gaze_cam_euler);
            }
          }
        }
        if(cur_spec_i == (int)SPEC.NEU && subtitle_i == subtitle_pause_i_voyager_1 && !advance_passed_voyager_1)
        {
          gaze_projection.transform.rotation = rotationFromEuler(getCamEuler(pluto[0].transform.position));
          gaze_reticle.SetActive(true);
          advance_trigger.position = gaze_reticle.transform.position;
          if(advance_trigger.tick(cam_reticle.transform.position,Time.deltaTime))
          {
            if(advance_trigger.just_triggered)
            {
              advance_passed_voyager_1 = true;
              gaze_reticle.SetActive(false);
              gaze_projection.transform.rotation = rotationFromEuler(gaze_cam_euler);
            }
          }
        }

        break;

      case (int)SCENE.NOTHING:


        break;

      case (int)SCENE.EXTREME:

        alert_t += Time.deltaTime;
        timer_t += Time.deltaTime;
        if(Mathf.Floor(alert_t) % 2 == 1)
          ar_alert.SetActive(false);
        else
          ar_alert.SetActive(true);

        float seconds_left = 60 - timer_t;
        if(seconds_left > 0)
        {
          if(blackhole_spec_triggered[cur_spec_i] == 0)
          {
            if(!gaze_reticle.activeSelf) gaze_reticle.SetActive(true);
            gaze_projection.transform.rotation = rotationFromEuler(getCamEuler(blackhole[0].transform.position));
            blackhole_trigger.position = gaze_reticle.transform.position;
            if(blackhole_trigger.tick(cam_reticle.transform.position,Time.deltaTime))
            {
              if(blackhole_trigger.just_triggered)
              {
                gaze_reticle.SetActive(false);
                blackhole_spec_triggered[cur_spec_i] = 1;
                blackhole_trigger.reset();
                bool all_done = true;
                for(int i = 0; i < (int)SPEC.COUNT; i++) all_done = (all_done && blackhole_spec_triggered[i] == 1);
                if(all_done)
                {
                  gaze_projection.transform.rotation = rotationFromEuler(anti_gaze_cam_euler);
                  gaze_reticle.SetActive(true);
                }
              }
            }
          }

          ar_timer_text.text = "00:" + Mathf.Floor(seconds_left) + ":" + Mathf.Floor((seconds_left - Mathf.Floor(seconds_left)) * 100);
          float bar_y = -2;
          float bar_x = -11;
          float bar_w = 23;
          bool play_end = true;
          for(int i = 0; i < (int)SPEC.COUNT; i++)
          {
            float t = blackhole_spec_triggered[i];
            if(t == 0 && i == cur_spec_i) t = Mathf.Min(1f,(blackhole_trigger.t_in/blackhole_trigger.t_max));
            ar_progress_lines[i].SetPosition(1, new Vector3(bar_x+bar_w*t, bar_y, 0));
            if(t == 1 && !ar_label_checks[i].activeSelf)
            {
              ar_label_checks[i].SetActive(true);
              ar_spec_checks[i].SetActive(true);
              PlaySFX(SFX.COMPLETE);
            }
            if(t < 1) play_end = false;
          }
          if(voiceovers_played[cur_scene_i,(int)SPEC.COUNT]) play_end = false;
          if(play_end)
          {
            if(voiceover_audiosource.isPlaying) voiceover_audiosource.Stop();
            voiceover_audiosource.clip = voiceovers[cur_scene_i,(int)SPEC.COUNT];
            voiceover_audiosource.volume = voiceover_vols[cur_scene_i,(int)SPEC.COUNT];
            voiceover_audiosource.Play();
            voiceover_was_playing = true;
            voiceovers_played[cur_scene_i,(int)SPEC.COUNT] = true;
            subtitle_i = 0;
            subtitle_t = 0;
            subtitles_text.text = "";
            subtitle_spec = (int)SPEC.COUNT;
          }
        }
        else if(in_fail_motion == 0)
        {
          in_fail_motion = 0.0001f;
          ar_timer_text.text = "XX:XX:XX";
        }

        float bhr_speed = 2.0f;
        for(int i = 0; i < (int)SPEC.COUNT; i++)
        {
          foreach(Transform child_transform in blackhole[i].transform)
          {
            child_transform.localRotation = Quaternion.Euler(10.0f, nwave_t_10*36*bhr_speed, 10.0f);
          }
        }

        break;

      case (int)SCENE.EARTH:

        earth[0].transform.position = anti_gaze_pt.normalized*600+new Vector3(0.0f,500.0f,0.0f);

        break;

      case (int)SCENE.CREDITS:

        credits_t += Time.deltaTime;
        if(credits_t > max_credits_t)
        {
          credits_t = 0;
          credits_i++;
          if(credits_i*2+1 < credit_strings.Length)
          {
            credits_text_0.text = credit_strings[credits_i*2+0];
            credits_text_1.text = credit_strings[credits_i*2+1];
          }
        }

        break;

    }

    if(!gaze_reticle.activeSelf && voiceovers_played[cur_scene_i,(int)SPEC.COUNT])
      gaze_reticle.SetActive(true);
  }

  void SetSpec(int spec)
  {
    //leaving actualy gameanalytics in (but commented out) so it's easy to find/replace them
    cur_spec_i = spec;
    switch(spec)
    {
      case (int)SPEC.GAM: spec_sel_reticle.transform.position = spec_gam_reticle.transform.position;
        //GameAnalytics.NewProgressionEvent (GAProgressionStatus.Start, "Universe", "Scene_" + cur_scene_i, "xray", 0);
        break;
      case (int)SPEC.VIZ: spec_sel_reticle.transform.position = spec_viz_reticle.transform.position;
        //GameAnalytics.NewProgressionEvent (GAProgressionStatus.Start, "Universe", "Scene_" + cur_scene_i, "viz", 0);
        break;
      case (int)SPEC.NEU: spec_sel_reticle.transform.position = spec_neu_reticle.transform.position;
        //GameAnalytics.NewProgressionEvent (GAProgressionStatus.Start, "Universe", "Scene_" + cur_scene_i, "neu", 0);
        break;
    }

    main_camera.GetComponent<Camera>().cullingMask = (1 << layers[cur_scene_i, cur_spec_i]) | (1 << default_layer);
    portal_camera_next.GetComponent<Camera>().cullingMask = (1 << layers[next_scene_i, (int)SPEC.VIZ]);
    main_camera_skybox.material = skyboxes[cur_scene_i, cur_spec_i];
    portal_camera_next_skybox.material = skyboxes[next_scene_i, (int)SPEC.VIZ];

    if(cur_scene_i == (int)SCENE.EXTREME) blackhole_trigger.reset();
  }

  float nwave_t_1 = 0;
  float nwave_t_10 = 0;
  void Update()
  {
    nwave_t_1  += Time.deltaTime;
    nwave_t_10 += Time.deltaTime;
    while(nwave_t_1  > 1)  nwave_t_1  -= 1f;
    while(nwave_t_10 > 10) nwave_t_10 -= 10f;

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
      HandleHMDUnmounted();
    }
    if(Input.GetKeyUp("space"))
    {
      HandleHMDMounted();
    }

    if(in_portal_motion > 0) in_portal_motion += Time.deltaTime * 0.8f;
    if(in_portal_motion > max_portal_motion)
    {
      out_portal_motion = in_portal_motion-max_portal_motion;
      if(out_portal_motion <= 0) out_portal_motion = 0.00001f;
      in_portal_motion = 0;
      if(cur_scene_i == (int)SCENE.CREDITS)
      {
        reStart();
        next_scene_i = (int)SCENE.ICE;
      }
      cur_scene_i = next_scene_i;
      next_scene_i = (next_scene_i + 1) % ((int)SCENE.COUNT);
      SetupScene();
    }
    if(out_portal_motion > 0)                 out_portal_motion += Time.deltaTime;
    if(out_portal_motion > max_portal_motion) out_portal_motion = 0;

    if(in_portal_motion > 0)
    {
      float t = in_portal_motion / (float)max_portal_motion;
      portal.transform.localPosition = new Vector3(default_portal_position.x, default_portal_position.y, Mathf.Lerp(default_portal_position.z, 0, t * t * t * t * t));
      float engulf = t - 1;
      engulf *= -engulf;
      engulf += 1;
      engulf /= 2;
      portal.transform.localScale = new Vector3(default_portal_scale.x * engulf, default_portal_scale.y * engulf, default_portal_scale.z * engulf);
    }
    else
    {
      portal.transform.localPosition = default_portal_position;
      portal.transform.localScale = new Vector3(0, 0, 0);
    }

    if(in_fail_motion > 0) in_fail_motion += Time.deltaTime * 0.2f;
    if(in_fail_motion > max_fail_motion)
    {
      //FAIL
      for(int i = 0; i < (int)SPEC.COUNT; i++)
        blackhole_spec_triggered[i] = 0;
      out_fail_motion = in_fail_motion-max_fail_motion;
      if(out_fail_motion <= 0) out_fail_motion = 0.00001f;
      in_fail_motion = 0;
      warp_trigger.t_in = 0;
      next_scene_i = cur_scene_i+1;
      scene_rots[next_scene_i] = 0;
      voiceovers_played[(int)SCENE.EXTREME,(int)SPEC.VIZ] = false;
      voiceovers_played[(int)SCENE.EXTREME,(int)SPEC.COUNT] = false;
      SetupScene();
    }
    if(out_fail_motion > 0)               out_fail_motion += Time.deltaTime;
    if(out_fail_motion > max_fail_motion) out_fail_motion = 0;

    UpdateScene();

    //subtitles
    if(dumb_delay_t > dumb_delay_t_max)
    {
      float old_sub_t = subtitle_t;
      subtitle_t += Time.deltaTime;
      if(
        old_sub_t  <  subtitle_cues_absolute[cur_scene_i,subtitle_spec,subtitle_i+1] &&
        subtitle_t >= subtitle_cues_absolute[cur_scene_i,subtitle_spec,subtitle_i+1]
      )
      {
        subtitle_i++;
        if(
          (cur_scene_i == (int)SCENE.ICE                                    && !advance_passed_ice_0     && subtitle_i == subtitle_pause_i_ice_0+1)     ||
          (cur_scene_i == (int)SCENE.ICE                                    && !advance_passed_ice_1     && subtitle_i == subtitle_pause_i_ice_1+1)     ||
          (cur_scene_i == (int)SCENE.VOYAGER && cur_spec_i == (int)SPEC.GAM && !advance_passed_voyager_0 && subtitle_i == subtitle_pause_i_voyager_0+1) ||
          (cur_scene_i == (int)SCENE.VOYAGER && cur_spec_i == (int)SPEC.NEU && !advance_passed_voyager_1 && subtitle_i == subtitle_pause_i_voyager_1+1)
        )
        {
          //freeze time
          if(!advance_paused)
          {
            if(voiceover_audiosource.isPlaying)
              voiceover_audiosource.Pause();
          }
          advance_paused = true;
          subtitle_i--;
          subtitle_t = subtitle_cues_absolute[cur_scene_i,subtitle_spec,subtitle_i+1]-0.0001f;
        }
        else
        {
          if(advance_paused) voiceover_audiosource.Play();
          advance_paused = false;
        }
        subtitles_text.text = subtitle_strings[cur_scene_i,subtitle_spec,subtitle_i];
      }
    }

    if(mouse_captured)
    {
      float in_x = Input.GetAxis("Mouse X") * 10;
      float in_y = Input.GetAxis("Mouse Y") * 10;
      if(!mouse_just_captured)
      {
        mouse_x += in_x;
        mouse_y += in_y;
      }
      else
      {
        if(in_x != 0 || in_y != 0)
          mouse_just_captured = false;
      }
    }

    Vector3 offset = new Vector3(
                       -main_camera.transform.localPosition.x,
                       -main_camera.transform.localPosition.y,
                       -main_camera.transform.localPosition.z);
    camera_house.transform.localPosition = offset + player_head;
    if(cur_scene_i == (int)SCENE.EXTREME)
      camera_house.transform.rotation = Quaternion.Euler((mouse_y - Screen.height / 2) * -2 + Random.Range(-extreme_camera_shake, extreme_camera_shake), (mouse_x - Screen.width / 2) * 2 + Random.Range(-extreme_camera_shake, extreme_camera_shake), 0 + Random.Range(-extreme_camera_shake, extreme_camera_shake));
    else
      camera_house.transform.rotation = Quaternion.Euler((mouse_y - Screen.height / 2) * -2, (mouse_x - Screen.width / 2) * 2, 0);

    look_ahead = main_camera.transform.rotation * default_look_ahead;
    lazy_look_ahead = Vector3.Lerp(lazy_look_ahead, look_ahead, 0.1f);
    very_lazy_look_ahead = Vector3.Lerp(very_lazy_look_ahead, look_ahead, 0.002f);
    if(cur_scene_i == (int)SCENE.EXTREME)
      helmet.transform.position = main_camera.transform.position + new Vector3(Random.Range(-extreme_helmet_shake, extreme_helmet_shake), Random.Range(-extreme_helmet_shake, extreme_helmet_shake), Random.Range(-extreme_helmet_shake, extreme_helmet_shake));
    else
      helmet.transform.position = main_camera.transform.position;
    helmet.transform.rotation = rotationFromEuler(getEuler(lazy_look_ahead));

    cam_euler = getCamEuler(cam_reticle.transform.position);
    spec_euler = getEuler(very_lazy_look_ahead);
    spec_euler.x = -3.141592f / 3f;
    spec_projection.transform.rotation = rotationFromEuler(spec_euler);

    if(in_portal_motion > 0)
    {
      float t = (float)in_portal_motion / max_portal_motion;
      flash_alpha = t * t * t * t * t;
    }
    else if(out_portal_motion > 0)
    {
      flash_alpha = 1.0f - ((float)out_portal_motion / max_portal_motion);
    }
    else if(in_fail_motion > 0)
    {
      float t = (float)in_fail_motion / max_fail_motion;
      flash_alpha = t * t * t * t * t;
    }
    else if(out_fail_motion > 0)
    {
      flash_alpha = 1.0f - ((float)out_fail_motion / max_fail_motion);
    }
    else
      flash_alpha = 0;
    flash_alpha *= 1.1f;
    if(flash_alpha > 1)
      flash_alpha = 1;
    flash_alpha = flash_alpha * flash_alpha;
    flash_alpha = flash_alpha * flash_alpha;
    alpha_material.SetFloat(alpha_id, flash_alpha);

    cam_spinner.transform.localScale = new Vector3(warp_trigger.shrink, warp_trigger.shrink, warp_trigger.shrink);
    cam_spinner.transform.localRotation = Quaternion.Euler(0, 0, warp_trigger.rot);
    warp_trigger.position = gaze_reticle.transform.position;

    if(
        (
        cur_scene_i != (int)SCENE.EARTH &&
        voiceovers_played[cur_scene_i,(int)SPEC.COUNT] &&
        in_fail_motion == 0 &&
        warp_trigger.tick(cam_reticle.transform.position,Time.deltaTime)
      )
        || //just use the above for normal use...
      (
        cur_scene_i == (int)SCENE.EARTH &&
        voiceovers_played[cur_scene_i,(int)SPEC.COUNT] &&
        !voiceover_was_playing
      ) //weird hack for end scene
    )
    {
      if(warp_trigger.just_in) warp_audiosource_ptr = PlaySFX(SFX.WARP);

      //advance
      if(warp_trigger.just_triggered)
      {
        if(warp_audiosource_ptr != null) warp_audiosource_ptr = null;
        if(in_portal_motion == 0 && out_portal_motion == 0)
        {
          in_portal_motion = Time.deltaTime;
          PreSetupNextScene();
        }
        in_fail_motion = 0;
      }
    }
    else
    {
      if(warp_audiosource_ptr != null && warp_audiosource_ptr.isPlaying)
      {
        warp_audiosource_ptr.Stop();
        warp_audiosource_ptr = null;
      }
    }

    float distance_viz = Vector3.Distance(spec_viz_reticle.transform.position, cam_reticle.transform.position);
    float distance_gam = Vector3.Distance(spec_gam_reticle.transform.position, cam_reticle.transform.position);
    float distance_neu = Vector3.Distance(spec_neu_reticle.transform.position, cam_reticle.transform.position);
    if(distance_viz < distance_gam && distance_viz < distance_neu) spec_trigger.position = spec_viz_reticle.transform.position;
    if(distance_gam < distance_viz && distance_gam < distance_neu) spec_trigger.position = spec_gam_reticle.transform.position;
    if(distance_neu < distance_gam && distance_neu < distance_viz) spec_trigger.position = spec_neu_reticle.transform.position;
    if(cur_scene_i == (int)SCENE.VOYAGER && !voiceovers_played[cur_scene_i,(int)SPEC.GAM]) spec_trigger.position = spec_gam_reticle.transform.position; //ensure you can only select gamma to start voyager selection

    if(
      !(cur_scene_i == (int)SCENE.VOYAGER && voiceover_was_playing) &&
      !advance_paused && spec_projection.activeSelf && spec_trigger.tick(cam_reticle.transform.position,Time.deltaTime)
      )
    {
      if(spec_trigger.just_triggered)
      {
        int old_spec = cur_spec_i;
        if(distance_gam <= distance_viz && distance_gam <= distance_neu) SetSpec((int)SPEC.GAM);
        if(distance_viz <= distance_gam && distance_viz <= distance_neu) SetSpec((int)SPEC.VIZ);
        if(distance_neu <= distance_gam && distance_neu <= distance_viz) SetSpec((int)SPEC.NEU);

        //spec switched
        if(old_spec != cur_spec_i)
        {
          PlaySFX(SFX.SELECT);
          if(!voiceovers_played[cur_scene_i,cur_spec_i] && dumb_delay_t > dumb_delay_t_max)
          {
            if(voiceover_audiosource.isPlaying) voiceover_audiosource.Stop();
            voiceover_audiosource.clip = voiceovers[cur_scene_i,cur_spec_i];
            voiceover_audiosource.volume = voiceover_vols[cur_scene_i,cur_spec_i];
            voiceover_audiosource.Play();
            voiceover_was_playing = true;
            voiceovers_played[cur_scene_i,cur_spec_i] = true;
            subtitle_i = 0;
            subtitle_t = 0;
            subtitles_text.text = "";
            subtitle_spec = cur_spec_i;
          }
          float old_time = music_audiosource.time;
          if(music_audiosource.isPlaying)
          {
            old_time = music_audiosource.time;
            music_audiosource.Stop();
          }
          music_audiosource.clip = musics[cur_scene_i,cur_spec_i];
          music_audiosource.volume = music_vols[cur_scene_i,cur_spec_i];
          music_audiosource.time = old_time;
          music_audiosource.Play();
          music_was_playing = true;
        }
      }
    }

    scene_rots[cur_scene_i] += scene_rot_deltas[cur_scene_i] * Time.deltaTime;
    while(scene_rots[cur_scene_i] > 3.14159265f * 2.0f) scene_rots[cur_scene_i] -= (3.14159265f * 2.0f);
    scene_rots[next_scene_i] += scene_rot_deltas[next_scene_i] * Time.deltaTime;
    while(scene_rots[next_scene_i] > 3.14159265f * 2.0f) scene_rots[next_scene_i] -= (3.14159265f * 2.0f);

    scene_groups[cur_scene_i, cur_spec_i].transform.position = new Vector3(0, 0, 0);
    scene_groups[cur_scene_i, cur_spec_i].transform.rotation = Quaternion.Euler(0f, 0f, 0f);

    scene_groups[cur_scene_i, cur_spec_i].transform.Translate(scene_centers[cur_scene_i]);
    scene_groups[cur_scene_i, cur_spec_i].transform.Rotate(0f, Mathf.Rad2Deg * scene_rots[cur_scene_i], 0f);
    scene_groups[cur_scene_i, cur_spec_i].transform.Translate(-scene_centers[cur_scene_i]);
    main_camera_skybox.material.SetFloat("_Rotation", -Mathf.Rad2Deg * scene_rots[cur_scene_i]);

    time_mod_twelve_pi = (time_mod_twelve_pi + Time.deltaTime) % (12.0f * 3.1415926535f);
    Shader.SetGlobalFloat(time_mod_twelve_pi_id, time_mod_twelve_pi);
    jitter_countdown -= Time.deltaTime;
    if(jitter_countdown <= 0.0f)
    {
      if(jitter_state == 1)
      {
        jitter_state = 0;
        jitter_countdown = Random.Range(jitter_min_downtime, jitter_max_downtime);
      }
      else
      {
        jitter_state = 1;
        jitter_countdown = Random.Range(jitter_min_uptime, jitter_max_uptime);
      }
    }
    if(jitter_state == 1 && cur_scene_i == (int)SCENE.EXTREME)
      jitter += Random.Range(-0.1f, 0.1f);
    else
      jitter = 0;
    Shader.SetGlobalFloat(jitter_id, jitter);

    //voiceover finished
    if(voiceover_was_playing)
    {
      if(!advance_paused && !voiceover_audiosource.isPlaying)
      {
        voiceover_was_playing = false;
        bool play_end = !voiceovers_played[cur_scene_i,(int)SPEC.COUNT];
        if(cur_scene_i == (int)SCENE.EXTREME) play_end = false;
        for(int i = 0; play_end && i < (int)SPEC.COUNT; i++)
        {
          if(!voiceovers_played[cur_scene_i,i]) play_end = false;
        }
        if(play_end)
        {
          voiceover_audiosource.clip = voiceovers[cur_scene_i,(int)SPEC.COUNT];
          voiceover_audiosource.volume = voiceover_vols[cur_scene_i,(int)SPEC.COUNT];
          voiceover_audiosource.Play();
          voiceover_was_playing = true;
          voiceovers_played[cur_scene_i,(int)SPEC.COUNT] = true;
          subtitle_i = 0;
          subtitle_t = 0;
          subtitles_text.text = "";
          subtitle_spec = (int)SPEC.COUNT;
        }
      }
    }
    else
    {
      if(dumb_delay_t < dumb_delay_t_max)
      {
        if(hmd_mounted) dumb_delay_t += Time.deltaTime;
        if(dumb_delay_t >= dumb_delay_t_max)//newly done with delay
        {
          voiceover_audiosource.clip = voiceovers[cur_scene_i,cur_spec_i];
          voiceover_audiosource.volume = voiceover_vols[cur_scene_i,cur_spec_i];
          voiceover_audiosource.Play();
          voiceover_was_playing = true;
          voiceovers_played[cur_scene_i,cur_spec_i] = true;
          subtitle_i = 0;
          subtitle_t = 0;
          subtitles_text.text = "";
          subtitle_spec = cur_spec_i;
        }
      }
    }
    if(music_was_playing)
    {
      if(!music_audiosource.isPlaying)
      {
        music_audiosource.clip = musics[cur_scene_i,cur_spec_i];
        music_audiosource.volume = music_vols[cur_scene_i,cur_spec_i];
        music_audiosource.Play();
        music_was_playing = true;
      }
    }

    float ball_t = (nwave_t_10%2f)/2f;
    gazeball.transform.position = Vector3.Lerp(gaze_pt,anti_gaze_pt,ball_t);
    Vector3 ball_pos = gazeball.transform.position;
    ball_pos -= grid_bulbs[grid_w-1,grid_h-1,0].transform.position;
    ball_pos /= 3f;
    ball_pos += grid_bulbs[grid_w-1,grid_h-1,0].transform.position;
    for(int i = 0; i < grid_w; i++)
    {
      for(int j = 0; j < grid_h; j++)
      {
        for(int k = 0; k < grid_d; k++)
        {
          float f = Vector3.Distance(ball_pos,grid_bulbs[i,j,k].transform.position);
          f = (30f-f)/30f;
          grid_s[i,j,k] = Mathf.Clamp(f,0.1f,1f);
        }
      }
    }

    for(int i = 0; i < grid_w; i++)
    {
      for(int j = 0; j < grid_h; j++)
      {
        for(int k = 0; k < grid_d; k++)
        {
          float s = grid_s[i,j,k]*default_grid_s;
          grid_bulbs[i,j,k].transform.localScale = new Vector3(s,s,s);
        }
      }
    }

    MapVols();
  }
}

