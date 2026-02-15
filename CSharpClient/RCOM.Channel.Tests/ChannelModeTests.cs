using System.Diagnostics.CodeAnalysis;
using RCOM.Channel;

namespace RCOM.Channel.Tests;

/// <summary>
/// ChannelMode 列挙型のテスト。
/// Peer モードと Group モードが正しく定義されていることを検証する。
/// </summary>
[TestClass]
public class ChannelModeTests
{
    /// <summary>
    /// ChannelMode に Peer 値が定義されていることを検証する。
    /// </summary>
    [TestMethod]
    public void ChannelMode_HasPeerValue()
    {
        Assert.IsTrue(Enum.IsDefined(typeof(ChannelMode), ChannelMode.Peer));
    }

    /// <summary>
    /// ChannelMode に Group 値が定義されていることを検証する。
    /// </summary>
    [TestMethod]
    public void ChannelMode_HasGroupValue()
    {
        Assert.IsTrue(Enum.IsDefined(typeof(ChannelMode), ChannelMode.Group));
    }

    /// <summary>
    /// Peer と Group が異なる整数値を持つことを検証する。
    /// </summary>
    [TestMethod]
    [SuppressMessage("", "MSTEST0032")]
    public void ChannelMode_PeerAndGroup_AreDifferentValues()
    {
        int peer = (int)ChannelMode.Peer;
        int group = (int)ChannelMode.Group;
        Assert.AreNotEqual(peer, group);
    }
}
