namespace DispatchService.Security;

public static class StaffRoles
{
    public const string Dispatcher = "Dispatcher";
    public const string Manager = "Manager";
    public const string TechnicianManagement = Dispatcher + "," + Manager;
}
