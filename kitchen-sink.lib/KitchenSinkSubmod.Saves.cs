using System.Collections.Generic;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.KitchenSinkLib;

public sealed partial class KitchenSinkSubmod : ISaveParticipantSource
{
    public IEnumerable<ISaveParticipant> SaveParticipants => new[]
    {
        new SaveParticipant<bool>("kitchen-sink", () => IvaForceRender.Enabled,
            () => IvaForceRender.Enabled = false, (state, _) => IvaForceRender.Enabled = state, order: 20)
    };
}
