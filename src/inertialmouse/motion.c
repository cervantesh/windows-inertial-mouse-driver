/*++
Module Name:

    motion.c

Abstract:

    Fixed-point inertial cursor dynamics.

--*/

#include "motion.h"

static
LONG
ImClampLong(
    _In_ LONG Value,
    _In_ LONG Minimum,
    _In_ LONG Maximum
    )
{
    if (Value < Minimum) {
        return Minimum;
    }

    if (Value > Maximum) {
        return Maximum;
    }

    return Value;
}

static
LONG
ImClampAddLong(
    _In_ LONG Value,
    _In_ LONG Delta,
    _In_ LONG Minimum,
    _In_ LONG Maximum
    )
{
    if ((Delta > 0) && (Value > Maximum - Delta)) {
        return Maximum;
    }

    if ((Delta < 0) && (Value < Minimum - Delta)) {
        return Minimum;
    }

    return ImClampLong(Value + Delta, Minimum, Maximum);
}

static
LONG
ImScaleDeltaToQ8(
    _In_ LONG Delta,
    _In_ LONG ScaleQ8,
    _In_ LONG MaxAbsQ8
    )
{
    LONG maxDelta;

    if (ScaleQ8 <= 0) {
        return 0;
    }

    maxDelta = MaxAbsQ8 / ScaleQ8;

    if (Delta > maxDelta) {
        return MaxAbsQ8;
    }

    if (Delta < -maxDelta) {
        return -MaxAbsQ8;
    }

    return Delta * ScaleQ8;
}

static
LONG
ImApplyFriction(
    _In_ LONG VelocityQ8,
    _In_ LONG FrictionQ8
    )
{
    if (VelocityQ8 > 0) {
        return (VelocityQ8 > FrictionQ8) ? (VelocityQ8 - FrictionQ8) : 0;
    }

    if (VelocityQ8 < 0) {
        return (VelocityQ8 < -FrictionQ8) ? (VelocityQ8 + FrictionQ8) : 0;
    }

    return 0;
}

static
LONG
ImEmitDelta(
    _Inout_ PLONG ResidualQ8,
    _In_ LONG VelocityQ8,
    _In_ LONG MaxVelocityQ8
    )
{
    LONG delta;

    *ResidualQ8 = ImClampAddLong(
        *ResidualQ8,
        VelocityQ8,
        -MaxVelocityQ8,
        MaxVelocityQ8
        );

    delta = *ResidualQ8 / IM_FIXED_SCALE;
    *ResidualQ8 -= delta * IM_FIXED_SCALE;

    return delta;
}

static
VOID
ImSavePacketTemplate(
    _Inout_ PIM_MOTION_STATE Motion,
    _In_ PMOUSE_INPUT_DATA Packet
    )
{
    Motion->PacketTemplate = *Packet;
    Motion->PacketTemplate.ButtonFlags = 0;
    Motion->PacketTemplate.ButtonData = 0;
    Motion->PacketTemplate.RawButtons = 0;
    Motion->PacketTemplate.LastX = 0;
    Motion->PacketTemplate.LastY = 0;
    Motion->PacketTemplate.Flags =
        (USHORT)(Motion->PacketTemplate.Flags & (USHORT)~MOUSE_MOVE_ABSOLUTE);
    Motion->HasPacketTemplate = TRUE;
}

VOID
ImMotionReset(
    _Out_ PIM_MOTION_STATE Motion
    )
{
    RtlZeroMemory(Motion, sizeof(*Motion));
}

BOOLEAN
ImMotionTransformPacket(
    _Inout_ PIM_MOTION_STATE Motion,
    _In_ const IM_CONFIG* Config,
    _Inout_ PMOUSE_INPUT_DATA Packet
    )
{
    LONG deltaXQ8;
    LONG deltaYQ8;

    if (!Config->Enabled || ((Packet->Flags & MOUSE_MOVE_ABSOLUTE) != 0)) {
        return FALSE;
    }

    if ((Packet->LastX == 0) && (Packet->LastY == 0)) {
        ImSavePacketTemplate(Motion, Packet);
        return FALSE;
    }

    deltaXQ8 = ImScaleDeltaToQ8(Packet->LastX, Config->AccelXQ8, Config->MaxVelocityXQ8);
    deltaYQ8 = ImScaleDeltaToQ8(Packet->LastY, Config->AccelYQ8, Config->MaxVelocityYQ8);

    Motion->VelocityXQ8 = ImClampAddLong(
        Motion->VelocityXQ8,
        deltaXQ8,
        -Config->MaxVelocityXQ8,
        Config->MaxVelocityXQ8
        );

    Motion->VelocityYQ8 = ImClampAddLong(
        Motion->VelocityYQ8,
        deltaYQ8,
        -Config->MaxVelocityYQ8,
        Config->MaxVelocityYQ8
        );

    Packet->LastX = ImEmitDelta(
        &Motion->ResidualXQ8,
        Motion->VelocityXQ8,
        Config->MaxVelocityXQ8
        );

    Packet->LastY = ImEmitDelta(
        &Motion->ResidualYQ8,
        Motion->VelocityYQ8,
        Config->MaxVelocityYQ8
        );

    ImSavePacketTemplate(Motion, Packet);
    return TRUE;
}

BOOLEAN
ImMotionBuildFrictionPacket(
    _Inout_ PIM_MOTION_STATE Motion,
    _In_ const IM_CONFIG* Config,
    _Out_ PMOUSE_INPUT_DATA Packet
    )
{
    LONG lastX;
    LONG lastY;

    if (!Config->Enabled || !Motion->HasPacketTemplate) {
        return FALSE;
    }

    lastX = ImEmitDelta(&Motion->ResidualXQ8, Motion->VelocityXQ8, Config->MaxVelocityXQ8);
    lastY = ImEmitDelta(&Motion->ResidualYQ8, Motion->VelocityYQ8, Config->MaxVelocityYQ8);

    Motion->VelocityXQ8 = ImApplyFriction(Motion->VelocityXQ8, Config->FrictionXQ8);
    Motion->VelocityYQ8 = ImApplyFriction(Motion->VelocityYQ8, Config->FrictionYQ8);

    if ((lastX == 0) && (lastY == 0)) {
        return FALSE;
    }

    *Packet = Motion->PacketTemplate;
    Packet->LastX = lastX;
    Packet->LastY = lastY;

    return TRUE;
}
