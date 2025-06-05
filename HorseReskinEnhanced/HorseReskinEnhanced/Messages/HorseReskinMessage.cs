using System;
using System.Collections.Generic;

namespace HorseReskinEnhanced.Messages
{
    public class HorseReskinMessage
    {
        public Guid HorseId { get; set; }
        public int SkinId { get; set; }
        public long RequestingPlayerId { get; set; }
        public bool ShowNotification { get; set; }
        public bool PlaySound { get; set; }

        public HorseReskinMessage() { }

        public HorseReskinMessage(Guid horseId, int skinId, long requestingPlayerId = 0, bool showNotification = true, bool playSound = true)
        {
            HorseId = horseId;
            SkinId = skinId;
            RequestingPlayerId = requestingPlayerId;
            ShowNotification = showNotification;
            PlaySound = playSound;
        }
    }

    public class SkinUpdateMessage
    {
        public Dictionary<Guid, int> SkinMap { get; set; } = new();
    }
}