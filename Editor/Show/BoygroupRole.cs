namespace T3.Editor.Show;

/// <summary>
/// A boygroup instance either shares the show (Master) or renders the setup a master sends (Slave).
/// The role is a setting, not a connection state: an enabled slave stays discoverable until a master
/// adds it.
/// </summary>
public enum BoygroupRole
{
    Master,
    Slave,
}
