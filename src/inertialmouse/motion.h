/*++
Module Name:

    motion.h

Abstract:

    Public interface for fixed-point inertial cursor dynamics.

--*/

#ifndef IM_MOTION_H
#define IM_MOTION_H

#include "inertialmouse.h"

BOOLEAN
ImMotionTransformPacket(
    _Inout_ PIM_MOTION_STATE Motion,
    _In_ const IM_CONFIG* Config,
    _Inout_ PMOUSE_INPUT_DATA Packet
    );

BOOLEAN
ImMotionBuildFrictionPacket(
    _Inout_ PIM_MOTION_STATE Motion,
    _In_ const IM_CONFIG* Config,
    _Out_ PMOUSE_INPUT_DATA Packet
    );

VOID
ImMotionReset(
    _Out_ PIM_MOTION_STATE Motion
    );

#endif
