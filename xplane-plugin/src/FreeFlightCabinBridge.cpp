#include "XPLMDataAccess.h"
#include "XPLMPlugin.h"
#include "XPLMProcessing.h"
#include "XPLMUtilities.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdio>
#include <cstring>

namespace
{
constexpr float kSampleIntervalSeconds = 0.1F;
constexpr int kResolveIntervalTicks = 50;
constexpr int kRecentChangeTicks = 300;

struct SignalSlot
{
    float value = 0.0F;
    int available = 0;
    bool pendingWrite = false;
};

enum class CandidateEncoding
{
    Ratio,
    Binary,
    FlightFactorSelector,
    ToLissDoorMode
};

struct Candidate
{
    const char* name;
    int arrayIndex;
    int priority;
    CandidateEncoding encoding = CandidateEncoding::Ratio;
    bool allowWrite = true;
    XPLMDataRef dataref = nullptr;
    float lastValue = 0.0F;
    int changeCount = 0;
    int lastChangedTick = -10000;
    bool sampled = false;
};

SignalSlot gSeatbelt;
SignalSlot gDoorL1;
SignalSlot gDoorL2;
SignalSlot gDoorL3;
SignalSlot gDoorL4;
SignalSlot gDoorL5;
SignalSlot gDoorR1;
SignalSlot gDoorR2;
SignalSlot gDoorR3;
SignalSlot gDoorR4;
SignalSlot gDoorR5;
int gPluginOnline = 1;
int gTick = 0;

std::array<Candidate, 6> gSeatbeltCandidates{{
    {"1-sim/anim/seatbeltLight", -1, 2000, CandidateEncoding::Binary, false},
    {"1-sim/ckpt/passSignsSeatbeltsSwitch/anim", -1, 110, CandidateEncoding::FlightFactorSelector},
    {"sim/cockpit2/annunciators/fasten_seatbelt", -1, 100, CandidateEncoding::Binary},
    {"sim/cockpit2/switches/fasten_seat_belts", -1, 90, CandidateEncoding::Binary},
    {"sim/cockpit/switches/fasten_seat_belts", -1, 85, CandidateEncoding::Binary},
    {"ckpt/oh/seatbelts/anim", -1, 70, CandidateEncoding::Binary, false}
}};

std::array<Candidate, 5> gDoorL1Candidates{{
    {"1-sim/anim/doorL1", -1, 2000},
    {"AirbusFBW/PaxDoorModeArray", 0, 1800, CandidateEncoding::ToLissDoorMode},
    {"1-sim/anim/FWDAccessDoor", -1, 95},
    {"1-sim/anim/doorFwd", -1, 90},
    {"sim/flightmodel2/misc/door_open_ratio", 0, 75}
}};

std::array<Candidate, 4> gDoorL2Candidates{{
    {"1-sim/anim/doorL2", -1, 2000},
    {"AirbusFBW/PaxDoorModeArray", 2, 1800, CandidateEncoding::ToLissDoorMode},
    {"1-sim/anim/doorAft", -1, 85},
    {"sim/flightmodel2/misc/door_open_ratio", 1, 75}
}};

std::array<Candidate, 2> gDoorL3Candidates{{
    {"1-sim/anim/doorL3", -1, 2000},
    {"sim/flightmodel2/misc/door_open_ratio", 2, 75}
}};
std::array<Candidate, 2> gDoorL4Candidates{{
    {"1-sim/anim/doorL4", -1, 2000},
    {"sim/flightmodel2/misc/door_open_ratio", 3, 75}
}};
std::array<Candidate, 2> gDoorL5Candidates{{
    {"1-sim/anim/doorL5", -1, 2000},
    {"sim/flightmodel2/misc/door_open_ratio", 4, 75}
}};
std::array<Candidate, 3> gDoorR1Candidates{{
    {"1-sim/anim/doorR1", -1, 2000},
    {"AirbusFBW/PaxDoorModeArray", 1, 1800, CandidateEncoding::ToLissDoorMode},
    {"sim/flightmodel2/misc/door_open_ratio", 5, 75}
}};
std::array<Candidate, 3> gDoorR2Candidates{{
    {"1-sim/anim/doorR2", -1, 2000},
    {"AirbusFBW/PaxDoorModeArray", 3, 1800, CandidateEncoding::ToLissDoorMode},
    {"sim/flightmodel2/misc/door_open_ratio", 6, 75}
}};
std::array<Candidate, 2> gDoorR3Candidates{{
    {"1-sim/anim/doorR3", -1, 2000},
    {"sim/flightmodel2/misc/door_open_ratio", 7, 75}
}};
std::array<Candidate, 2> gDoorR4Candidates{{
    {"1-sim/anim/doorR4", -1, 2000},
    {"sim/flightmodel2/misc/door_open_ratio", 8, 75}
}};
std::array<Candidate, 2> gDoorR5Candidates{{
    {"1-sim/anim/doorR5", -1, 2000},
    {"sim/flightmodel2/misc/door_open_ratio", 9, 75}
}};

std::array<XPLMDataRef, 23> gPublishedDatarefs{};

float ClampRatio(float value)
{
    if (!std::isfinite(value)) return 0.0F;
    if (value > 1.5F) value /= 100.0F;
    return std::clamp(value, 0.0F, 1.0F);
}

bool ReadCandidate(Candidate& candidate, float& value)
{
    if (candidate.dataref == nullptr) return false;
    const auto types = XPLMGetDataRefTypes(candidate.dataref);
    if (candidate.arrayIndex >= 0 && (types & xplmType_FloatArray) != 0)
    {
        float item = 0.0F;
        if (XPLMGetDatavf(candidate.dataref, &item, candidate.arrayIndex, 1) != 1) return false;
        value = item;
        return true;
    }
    if (candidate.arrayIndex >= 0 && (types & xplmType_IntArray) != 0)
    {
        int item = 0;
        if (XPLMGetDatavi(candidate.dataref, &item, candidate.arrayIndex, 1) != 1) return false;
        value = static_cast<float>(item);
        return true;
    }
    if ((types & xplmType_Float) != 0) value = XPLMGetDataf(candidate.dataref);
    else if ((types & xplmType_Double) != 0) value = static_cast<float>(XPLMGetDatad(candidate.dataref));
    else if ((types & xplmType_Int) != 0) value = static_cast<float>(XPLMGetDatai(candidate.dataref));
    else return false;
    return std::isfinite(value);
}

bool WriteCandidate(Candidate& candidate, float value)
{
    if (!candidate.allowWrite || candidate.dataref == nullptr || XPLMCanWriteDataRef(candidate.dataref) == 0) return false;
    const float encodedValue = candidate.encoding == CandidateEncoding::FlightFactorSelector ||
                               candidate.encoding == CandidateEncoding::ToLissDoorMode
        ? (value >= 0.5F ? 2.0F : 0.0F)
        : value;
    const auto types = XPLMGetDataRefTypes(candidate.dataref);
    if (candidate.arrayIndex >= 0 && (types & xplmType_FloatArray) != 0)
    {
        float item = encodedValue;
        XPLMSetDatavf(candidate.dataref, &item, candidate.arrayIndex, 1);
        return true;
    }
    if (candidate.arrayIndex >= 0 && (types & xplmType_IntArray) != 0)
    {
        int item = static_cast<int>(encodedValue);
        XPLMSetDatavi(candidate.dataref, &item, candidate.arrayIndex, 1);
        return true;
    }
    if ((types & xplmType_Float) != 0) XPLMSetDataf(candidate.dataref, encodedValue);
    else if ((types & xplmType_Double) != 0) XPLMSetDatad(candidate.dataref, encodedValue);
    else if ((types & xplmType_Int) != 0) XPLMSetDatai(candidate.dataref, static_cast<int>(encodedValue));
    else return false;
    return true;
}

template <std::size_t Size>
void ResolveCandidates(std::array<Candidate, Size>& candidates)
{
    for (auto& candidate : candidates)
    {
        if (candidate.dataref == nullptr) candidate.dataref = XPLMFindDataRef(candidate.name);
    }
}

template <std::size_t Size>
void ResetCandidates(std::array<Candidate, Size>& candidates)
{
    for (auto& candidate : candidates)
    {
        candidate.dataref = nullptr;
        candidate.lastValue = 0.0F;
        candidate.changeCount = 0;
        candidate.lastChangedTick = -10000;
        candidate.sampled = false;
    }
}

template <std::size_t Size>
void SampleSignal(std::array<Candidate, Size>& candidates, SignalSlot& output, bool binary)
{
    Candidate* selected = nullptr;
    int selectedScore = -1;
    float selectedValue = output.value;
    for (auto& candidate : candidates)
    {
        float value = 0.0F;
        if (!ReadCandidate(candidate, value)) continue;
        if (binary)
        {
            const float threshold = candidate.encoding == CandidateEncoding::FlightFactorSelector ||
                                    candidate.encoding == CandidateEncoding::ToLissDoorMode
                ? 1.5F
                : 0.5F;
            value = value >= threshold ? 1.0F : 0.0F;
        }
        else
        {
            value = candidate.encoding == CandidateEncoding::ToLissDoorMode
                ? (value >= 1.5F ? 1.0F : 0.0F)
                : ClampRatio(value);
        }
        if (candidate.sampled && std::abs(value - candidate.lastValue) >= 0.25F)
        {
            candidate.changeCount = std::min(candidate.changeCount + 1, 20);
            candidate.lastChangedTick = gTick;
        }
        candidate.lastValue = value;
        candidate.sampled = true;
        const int recentBonus = gTick - candidate.lastChangedTick <= kRecentChangeTicks ? 200 : 0;
        const int score = candidate.priority + (candidate.changeCount * 30) + recentBonus;
        if (score > selectedScore)
        {
            selected = &candidate;
            selectedScore = score;
            selectedValue = value;
        }
    }
    output.available = selected == nullptr ? 0 : 1;
    if (selected != nullptr) output.value = selectedValue;
}

template <std::size_t Size>
void ApplyPendingWrite(std::array<Candidate, Size>& candidates, SignalSlot& slot)
{
    if (!slot.pendingWrite) return;
    for (auto& candidate : candidates) WriteCandidate(candidate, slot.value);
    slot.pendingWrite = false;
}

int ReadInt(void* refcon) { return *static_cast<int*>(refcon); }
float ReadFloat(void* refcon) { return static_cast<SignalSlot*>(refcon)->value; }
void WriteFloat(void* refcon, float value)
{
    auto* slot = static_cast<SignalSlot*>(refcon);
    slot->value = ClampRatio(value);
    slot->pendingWrite = true;
}

XPLMDataRef RegisterInt(const char* name, int* value) {
    return XPLMRegisterDataAccessor(name, xplmType_Int, 0, ReadInt, nullptr, nullptr, nullptr, nullptr, nullptr,
        nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, value, nullptr);
}

XPLMDataRef RegisterFloat(const char* name, SignalSlot* slot, bool writable) {
    return XPLMRegisterDataAccessor(name, xplmType_Float, writable ? 1 : 0, nullptr, nullptr, ReadFloat,
        writable ? WriteFloat : nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr,
        slot, writable ? slot : nullptr);
}

void ResolveAllCandidates()
{
    ResolveCandidates(gSeatbeltCandidates);
    ResolveCandidates(gDoorL1Candidates);
    ResolveCandidates(gDoorL2Candidates);
    ResolveCandidates(gDoorL3Candidates);
    ResolveCandidates(gDoorL4Candidates);
    ResolveCandidates(gDoorL5Candidates);
    ResolveCandidates(gDoorR1Candidates);
    ResolveCandidates(gDoorR2Candidates);
    ResolveCandidates(gDoorR3Candidates);
    ResolveCandidates(gDoorR4Candidates);
    ResolveCandidates(gDoorR5Candidates);
}

float FlightLoop(float, float, int, void*)
{
    ++gTick;
    if (gTick == 1 || gTick % kResolveIntervalTicks == 0) ResolveAllCandidates();
    ApplyPendingWrite(gSeatbeltCandidates, gSeatbelt);
    ApplyPendingWrite(gDoorL1Candidates, gDoorL1);
    ApplyPendingWrite(gDoorL2Candidates, gDoorL2);
    ApplyPendingWrite(gDoorL3Candidates, gDoorL3);
    ApplyPendingWrite(gDoorL4Candidates, gDoorL4);
    ApplyPendingWrite(gDoorL5Candidates, gDoorL5);
    ApplyPendingWrite(gDoorR1Candidates, gDoorR1);
    ApplyPendingWrite(gDoorR2Candidates, gDoorR2);
    ApplyPendingWrite(gDoorR3Candidates, gDoorR3);
    ApplyPendingWrite(gDoorR4Candidates, gDoorR4);
    ApplyPendingWrite(gDoorR5Candidates, gDoorR5);
    SampleSignal(gSeatbeltCandidates, gSeatbelt, true);
    SampleSignal(gDoorL1Candidates, gDoorL1, false);
    SampleSignal(gDoorL2Candidates, gDoorL2, false);
    SampleSignal(gDoorL3Candidates, gDoorL3, false);
    SampleSignal(gDoorL4Candidates, gDoorL4, false);
    SampleSignal(gDoorL5Candidates, gDoorL5, false);
    SampleSignal(gDoorR1Candidates, gDoorR1, false);
    SampleSignal(gDoorR2Candidates, gDoorR2, false);
    SampleSignal(gDoorR3Candidates, gDoorR3, false);
    SampleSignal(gDoorR4Candidates, gDoorR4, false);
    SampleSignal(gDoorR5Candidates, gDoorR5, false);
    return kSampleIntervalSeconds;
}

void ResetForAircraft()
{
    ResetCandidates(gSeatbeltCandidates);
    ResetCandidates(gDoorL1Candidates);
    ResetCandidates(gDoorL2Candidates);
    ResetCandidates(gDoorL3Candidates);
    ResetCandidates(gDoorL4Candidates);
    ResetCandidates(gDoorL5Candidates);
    ResetCandidates(gDoorR1Candidates);
    ResetCandidates(gDoorR2Candidates);
    ResetCandidates(gDoorR3Candidates);
    ResetCandidates(gDoorR4Candidates);
    ResetCandidates(gDoorR5Candidates);
    gSeatbelt.available = gDoorL1.available = gDoorL2.available = gDoorL3.available =
        gDoorL4.available = gDoorL5.available = gDoorR1.available = gDoorR2.available =
        gDoorR3.available = gDoorR4.available = gDoorR5.available = 0;
    gTick = 0;
}
}

PLUGIN_API int XPluginStart(char* outName, char* outSignature, char* outDescription)
{
    std::snprintf(outName, 256, "FreeFlight Cabin Bridge");
    std::snprintf(outSignature, 256, "com.freeflight.cabinbridge");
    std::snprintf(outDescription, 256, "Publishes stable FreeFlight door and seat-belt datarefs.");
    gPublishedDatarefs = {
        RegisterInt("freeflight/cabin/plugin_online", &gPluginOnline),
        RegisterInt("freeflight/cabin/seatbelt_available", &gSeatbelt.available),
        RegisterFloat("freeflight/cabin/seatbelt_sign", &gSeatbelt, true),
        RegisterInt("freeflight/cabin/door_l1_available", &gDoorL1.available),
        RegisterFloat("freeflight/cabin/door_l1_ratio", &gDoorL1, true),
        RegisterInt("freeflight/cabin/door_l2_available", &gDoorL2.available),
        RegisterFloat("freeflight/cabin/door_l2_ratio", &gDoorL2, true),
        RegisterInt("freeflight/cabin/door_l3_available", &gDoorL3.available),
        RegisterFloat("freeflight/cabin/door_l3_ratio", &gDoorL3, true),
        RegisterInt("freeflight/cabin/door_l4_available", &gDoorL4.available),
        RegisterFloat("freeflight/cabin/door_l4_ratio", &gDoorL4, true),
        RegisterInt("freeflight/cabin/door_l5_available", &gDoorL5.available),
        RegisterFloat("freeflight/cabin/door_l5_ratio", &gDoorL5, true),
        RegisterInt("freeflight/cabin/door_r1_available", &gDoorR1.available),
        RegisterFloat("freeflight/cabin/door_r1_ratio", &gDoorR1, true),
        RegisterInt("freeflight/cabin/door_r2_available", &gDoorR2.available),
        RegisterFloat("freeflight/cabin/door_r2_ratio", &gDoorR2, true),
        RegisterInt("freeflight/cabin/door_r3_available", &gDoorR3.available),
        RegisterFloat("freeflight/cabin/door_r3_ratio", &gDoorR3, true),
        RegisterInt("freeflight/cabin/door_r4_available", &gDoorR4.available),
        RegisterFloat("freeflight/cabin/door_r4_ratio", &gDoorR4, true),
        RegisterInt("freeflight/cabin/door_r5_available", &gDoorR5.available),
        RegisterFloat("freeflight/cabin/door_r5_ratio", &gDoorR5, true)
    };
    XPLMDebugString("FreeFlight Cabin Bridge: stable cabin datarefs registered.\n");
    return 1;
}

PLUGIN_API void XPluginStop(void)
{
    XPLMUnregisterFlightLoopCallback(FlightLoop, nullptr);
    for (const auto dataref : gPublishedDatarefs) if (dataref != nullptr) XPLMUnregisterDataAccessor(dataref);
}

PLUGIN_API int XPluginEnable(void)
{
    ResetForAircraft();
    ResolveAllCandidates();
    XPLMRegisterFlightLoopCallback(FlightLoop, kSampleIntervalSeconds, nullptr);
    return 1;
}

PLUGIN_API void XPluginDisable(void)
{
    XPLMUnregisterFlightLoopCallback(FlightLoop, nullptr);
}

PLUGIN_API void XPluginReceiveMessage(XPLMPluginID, int message, void*)
{
    if (message == XPLM_MSG_PLANE_LOADED) ResetForAircraft();
}
