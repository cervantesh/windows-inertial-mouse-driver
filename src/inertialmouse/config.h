/*++
Module Name:

    config.h

Abstract:

    Public interface for registry-backed driver configuration.

--*/

#ifndef IM_CONFIG_H
#define IM_CONFIG_H

#include "inertialmouse.h"

VOID
ImConfigLoad(
    _In_ WDFDEVICE Device,
    _Out_ PIM_CONFIG Config
    );

#endif
