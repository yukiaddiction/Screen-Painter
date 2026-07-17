namespace Screen_Painter.Models;

public enum TriggerType
{
    Timer,
    ScreenAwake,
    OnVisible
}

public enum OnVisibleMode
{
    None,
    Reveal,
    Hide
}

public enum StorageType
{
    Local,
    WebDav,
    OAuthCloud
}
