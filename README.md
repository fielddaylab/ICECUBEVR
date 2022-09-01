# ICECUBEVR

*Player Events (Play choices and actions)
headset_on - this sets the 0 time for seconds_from_launch)
language_selected (string langugage, float seconds_from_launch)
start(float seconds_from_launch)
viewport_data ([float seconds_from_launch, float head_pos, float head_orientation]) 
gaze_point_selected (string gaze_point_name)


*Progression Events (An achievement is made, a goal is met, time advaces)



*Feedback Events (The system is communicating something to the player formatively)
script_audio_started (int/string clip_identifier)
script_audio_complete (int/string clip_identifier)
caption_displayed (string caption)
gaze_point_displayed (string gaze_point_name, float head_pos, float head_orientation)
