using System.Collections.Generic;
using NUnit.Framework;
using MuseDesk.Voice;

namespace MuseDesk.Tests.Voice
{
    public class VoiceAddressingTests
    {
        VoiceAddressing _a;
        List<string> _log;

        [SetUp]
        public void Setup()
        {
            _a = new VoiceAddressing(5f);
            _log = new List<string>();
            _a.ListeningStarted += t => _log.Add("start:" + t);
            _a.ListeningEnded += t => _log.Add("end:" + t);
        }

        [Test]
        public void Nobody_addressed_when_mic_opens()
        {
            _a.MicOpened(0f);
            Assert.AreEqual(VoiceAgentTarget.None, _a.AddressedAgent);
            Assert.IsEmpty(_log);
        }

        [Test]
        public void Name_mention_addresses_agent()
        {
            _a.MicOpened(0f);
            _a.Hear("developer what are you doing", 1f);
            Assert.AreEqual(VoiceAgentTarget.Developer, _a.AddressedAgent);
            CollectionAssert.AreEqual(new[] { "start:Developer" }, _log);
        }

        [Test]
        public void Mic_close_ends_listening()
        {
            _a.MicOpened(0f);
            _a.Hear("tester", 1f);
            _a.MicClosed(2f);
            Assert.AreEqual(VoiceAgentTarget.None, _a.AddressedAgent);
            CollectionAssert.AreEqual(new[] { "start:Tester", "end:Tester" }, _log);
        }

        [Test]
        public void Mention_ignored_while_mic_closed()
        {
            _a.Hear("developer", 0f);
            Assert.AreEqual(VoiceAgentTarget.None, _a.AddressedAgent);
            Assert.IsEmpty(_log);
        }

        [Test]
        public void Timeout_ends_listening()
        {
            _a.MicOpened(0f);
            _a.Hear("researcher", 1f);
            _a.Tick(5f);
            Assert.AreEqual(VoiceAgentTarget.Researcher, _a.AddressedAgent);
            _a.Tick(6.5f);
            Assert.AreEqual(VoiceAgentTarget.None, _a.AddressedAgent);
            CollectionAssert.AreEqual(new[] { "start:Researcher", "end:Researcher" }, _log);
        }

        [Test]
        public void Repeated_mention_refreshes_timeout()
        {
            _a.MicOpened(0f);
            _a.Hear("tester", 0f);
            _a.Hear("tester again", 4f);
            _a.Tick(8f);
            Assert.AreEqual(VoiceAgentTarget.Tester, _a.AddressedAgent);
            Assert.AreEqual(1, _log.Count);
        }

        [Test]
        public void Switching_agent_ends_old_then_starts_new()
        {
            _a.MicOpened(0f);
            _a.Hear("developer", 1f);
            _a.Hear("no the tester", 2f);
            CollectionAssert.AreEqual(new[] { "start:Developer", "end:Developer", "start:Tester" }, _log);
        }

        [Test]
        public void Gaze_addresses_when_mic_open()
        {
            _a.MicOpened(0f);
            _a.SetGaze(VoiceAgentTarget.Tester, 1f);
            Assert.AreEqual(VoiceAgentTarget.Tester, _a.AddressedAgent);
        }

        [Test]
        public void Fresh_gaze_before_mic_counts_at_open()
        {
            _a.SetGaze(VoiceAgentTarget.Developer, 1f);
            Assert.IsEmpty(_log);
            _a.MicOpened(2f);
            CollectionAssert.AreEqual(new[] { "start:Developer" }, _log);
        }

        [Test]
        public void Stale_gaze_before_mic_is_ignored()
        {
            _a.SetGaze(VoiceAgentTarget.Developer, 1f);
            _a.MicOpened(20f);
            Assert.AreEqual(VoiceAgentTarget.None, _a.AddressedAgent);
        }

        [Test]
        public void Mention_beats_gaze()
        {
            _a.MicOpened(0f);
            _a.SetGaze(VoiceAgentTarget.Tester, 1f);
            _a.Hear("developer", 2f);
            Assert.AreEqual(VoiceAgentTarget.Developer, _a.AddressedAgent);
        }

        [Test]
        public void Losing_gaze_ends_gaze_addressing()
        {
            _a.MicOpened(0f);
            _a.SetGaze(VoiceAgentTarget.Tester, 1f);
            _a.SetGaze(VoiceAgentTarget.None, 2f);
            Assert.AreEqual(VoiceAgentTarget.None, _a.AddressedAgent);
        }

        [Test]
        public void Mic_reopen_forgets_old_mention()
        {
            _a.MicOpened(0f);
            _a.Hear("tester", 1f);
            _a.MicClosed(2f);
            _a.MicOpened(3f);
            Assert.AreEqual(VoiceAgentTarget.None, _a.AddressedAgent);
        }
    }
}
