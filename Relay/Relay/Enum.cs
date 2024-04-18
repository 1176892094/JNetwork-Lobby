// *********************************************************************************
// # Project: Relay
// # Unity: 2022.3.5f1c1
// # Author: jinyijie
// # Version: 1.0.0
// # History: 2024-4-18  1:12
// # Copyright: 2024, jinyijie
// # Description: This is an automatically generated comment.
// *********************************************************************************

namespace JFramework.Net
{
    public enum Channel : sbyte
    {
        Reliable = 1,
        Unreliable = 2
    }

    public enum Regions : byte
    {
        Any,
        NorthAmerica,
        SouthAmerica,
        Europe,
        Asia,
        Africa,
        Oceania
    }

    public enum OpCode : byte
    {
        Default = 0,
        RequestID = 1,
        JoinServer = 2,
        SendData = 3,
        GetID = 4,
        ServerJoined = 5,
        GetData = 6,
        CreateRoom = 7,
        ServerLeft = 8,
        PlayerDisconnected = 9,
        RoomCreated = 10,
        LeaveRoom = 11,
        KickPlayer = 12,
        AuthenticationRequest = 13,
        AuthenticationResponse = 14,
        Authenticated = 17,
        UpdateRoomData = 18,
        ServerConnectionData = 19,
        RequestNATConnection = 20,
        DirectConnectIP = 21
    }
}