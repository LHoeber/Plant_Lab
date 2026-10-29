/// <summary>
/// Actions within a trial (one per tick): turn the observation camera, or nothing.
/// The choice of a button combination happens between trials (one discrete choice per trial, see TrialController).
/// </summary>
public static class LabActions
{
    public const int CameraLeft = 0, CameraRight = 1, CameraUp = 2, CameraDown = 3;
    public const int NoOp = 4;
    public const int Count = 5;

    public static bool IsCamera(int action) => action >= CameraLeft && action <= CameraDown;

    public static string Name(int action) => action switch
    {
        CameraLeft => "camera left", CameraRight => "camera right", CameraUp => "camera up", CameraDown => "camera down",
        _ => "no-op",
    };
}
