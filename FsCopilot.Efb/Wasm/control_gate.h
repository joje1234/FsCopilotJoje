#pragma once

#include "control_protocol.h"

namespace fsc_controls
{
// Adapter configures the simulator and then issues a RequestSystemState barrier.
// Only its matching reply commits ownership. Observing an event never changes it.
template<class Adapter> class control_gate
{
public:
    explicit control_gate(Adapter& adapter) : adapter_(adapter) {}

    bool steering_blocked = false;
    bool quadrant_blocked = false;
    bool ready = false;
    bool applying = false;
    uint32_t revision = 0;
    uint32_t barrier = 0;
    command pending{};

    void initialize()
    {
        ready = false;
        begin(command{}, false, false);
    }

    bool request(const command& cmd)
    {
        if (!ready || applying || (cmd.operation != action::set && cmd.operation != action::restore && cmd.operation != action::force)) return false;
        bool steering = steering_blocked;
        bool quadrant = quadrant_blocked;
        if (cmd.operation == action::restore) steering = quadrant = false;
        else if (cmd.target == group::steering) steering = cmd.operation == action::force ? false : cmd.blocked;
        else quadrant = cmd.operation == action::force ? false : cmd.blocked;
        return begin(cmd, steering, quadrant);
    }

    bool confirm(uint32_t id)
    {
        if (!applying || id != barrier) return false;
        steering_blocked = next_steering_;
        quadrant_blocked = next_quadrant_;
        applying = false;
        ready = true;
        ++revision;
        return true;
    }

    void fail()
    {
        // Closing our dedicated connection removes all its notification groups.
        adapter_.release();
        ready = false;
        applying = false;
        steering_blocked = quadrant_blocked = false;
        ++revision;
    }

private:
    Adapter& adapter_;
    bool next_steering_ = false;
    bool next_quadrant_ = false;

    bool begin(const command& cmd, bool steering, bool quadrant)
    {
        pending = cmd;
        next_steering_ = steering;
        next_quadrant_ = quadrant;
        applying = true;
        ++barrier;
        if (!adapter_.configure(steering, quadrant, barrier))
        {
            fail();
            return false;
        }
        return true;
    }
};
}
