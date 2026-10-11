/// <summary>
/// The participant's / agent's action space: 9 discrete actions, one per tick.
/// 0-3 move the observation camera, 4-7 press intervention buttons A-D (hidden mapping), 8 = no-op.
/// </summary>
public static class LabActions
{
    public const int CameraLeft = 0, CameraRight = 1, CameraUp = 2, CameraDown = 3;
    public const int ButtonA = 4, ButtonB = 5, ButtonC = 6, ButtonD = 7;
    public const int NoOp = 8;
    public const int Count = 9;

    public static bool IsCamera(int action) => action >= CameraLeft && action <= CameraDown;
    public static bool IsIntervention(int action) => action >= ButtonA && action <= ButtonD;
    /// <summary>Intervention button identity 0-3 (A-D) of an action 4-7.</summary>
    public static int ButtonIndex(int action) => action - ButtonA;

    public static string Name(int action) => action switch
    {
        CameraLeft => "camera left", CameraRight => "camera right", CameraUp => "camera up", CameraDown => "camera down",
        ButtonA => "button A", ButtonB => "button B", ButtonC => "button C", ButtonD => "button D",
        _ => "no-op",
    };
}
