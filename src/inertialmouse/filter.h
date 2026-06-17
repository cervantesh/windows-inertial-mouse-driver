/*++
Module Name:

    filter.h

Abstract:

    Public interface for the mouse packet filter callbacks.

--*/

#ifndef IM_FILTER_H
#define IM_FILTER_H

#include "inertialmouse.h"

EVT_WDF_TIMER ImFrictionTimer;

VOID
ImServiceCallback(
    _In_ PDEVICE_OBJECT DeviceObject,
    _In_ PMOUSE_INPUT_DATA InputDataStart,
    _In_ PMOUSE_INPUT_DATA InputDataEnd,
    _Inout_ PULONG InputDataConsumed
    );

#endif
