/// <summary>
/// The participant's / agent's action space: one discrete action per tick.
/// 0-3 move the observation camera, 4-9 press intervention buttons A-F (hidden mapping), 10 = no-op.
/// </summary>
public static class LabActions
{
    /// <summary>Number of intervention buttons (A, B, ...). Placeholder count, may change with the console design.</summary>
    public const int InterventionCount = 6;

    public const int CameraLeft = 0, CameraRight = 1, CameraUp = 2, CameraDown = 3;
    public const int ButtonA = 4;
    public const int NoOp = ButtonA + InterventionCount;
    public const int Count = NoOp + 1;

    public static bool IsCamera(int action) => action >= CameraLeft && action <= CameraDown;
    public static bool IsIntervention(int action) => action >= ButtonA && action < ButtonA + InterventionCount;
    /// <summary>Intervention button identity 0, 1, ... (A, B, ...) of an intervention action.</summary>
    public static int ButtonIndex(int action) => action - ButtonA;
    /// <summary>Letter of intervention button i: 0 -> 'A'.</summary>
    public static char ButtonLetter(int i) => (char)('A' + i);

    public static string Name(int action) => action switch
    {
        CameraLeft => "camera left", CameraRight => "camera right", CameraUp => "camera up", CameraDown => "camera down",
        _ when IsIntervention(action) => $"button {ButtonLetter(ButtonIndex(action))}",
        _ => "no-op",
    };
}
