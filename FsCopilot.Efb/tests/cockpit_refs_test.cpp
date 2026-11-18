#include <cassert>
#include <cmath>
#include <iostream>
#include <initializer_list>
#include "../Wasm/control_cockpit_refs.h"
using namespace fsc_controls;
int main() {
    const auto* profile = &definition_profiles[0];
    double values[target_count]{};
    read_cockpit_references(values, nullptr);
    assert(!fake_vars::state().lookups && !fake_vars::state().gets);
    for (const auto fraction : {0.0, .005, .5, 1.0}) {
        for (const bool percent_storage : {false, true}) {
            fake_vars::state() = fake_vars::registry{};
            for (unsigned engine = 0; engine < 2; ++engine) {
                fake_vars::state().percent_storage[engine] = percent_storage;
                fake_vars::state().value[engine] = fraction * (percent_storage ? 100 : 1);
            }
            read_cockpit_references(values, profile);
            assert(values[11] == fraction * 100 && values[14] == fraction * 100);
        }
    }
    for (unsigned failure = 0; failure < 4; ++failure) {
        fake_vars::state() = fake_vars::registry{};
        fake_vars::state().value[0] = fake_vars::state().value[1] = 50;
        if (failure == 0) fake_vars::state().available[0] = false;
        if (failure == 1) fake_vars::state().failed[0] = true;
        if (failure == 2) fake_vars::state().value[0] = 150;
        if (failure == 3) fake_vars::state().invalid_unit = true;
        read_cockpit_references(values, profile);
        assert(std::isnan(values[11]));
        if (failure != 3) assert(values[14] == 50);
    }
    std::cout << "Typed cockpit references passed: explicit Percent reads across fraction/percent storage, sub-one-percent positions, absent/failed/range-invalid references; no registration or setter API.\n";
}
