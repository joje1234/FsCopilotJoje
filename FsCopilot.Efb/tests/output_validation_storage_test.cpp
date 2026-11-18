#define _CRT_SECURE_NO_WARNINGS
#include <cassert>
#include <iostream>
#include "../Wasm/control_output_validation_storage.h"
using namespace fsc_controls;
namespace {
const char* prefix = "artifacts/efb-control-test/tests/output-validation-fixture";
void replace_slot(unsigned slot, output_validation_record value, bool corrupt = false, bool extra = false) {
    char path[600]{}; std::snprintf(path, sizeof(path), "%s.%u.bin", prefix, slot);
    value.checksum = profile_hash(&value, offsetof(output_validation_record, checksum));
    if (corrupt) value.checksum ^= 1;
    auto* file = std::fopen(path, "wb"); assert(file);
    assert(std::fwrite(&value, 1, sizeof(value), file) == sizeof(value));
    if (extra) assert(std::fputc(0, file) == 0);
    assert(std::fclose(file) == 0);
}
void clean() {
    for (unsigned slot = 0; slot < 2; ++slot) {
        char path[600]{}; std::snprintf(path, sizeof(path), "%s.%u.bin", prefix, slot); std::remove(path);
    }
}
}
int main() {
    clean();
    output_validation_record saved{}, loaded{};
    assert(profile_identity("SimObjects\\Airplanes\\Test\\aircraft.cfg", 100, saved.aircraft)); saved.engines = 2;
    assert(!read_output_validation(prefix, saved.aircraft, 2, loaded));
    for (unsigned index = 0; index < 4; ++index) saved.proofs[index] = 100 + index;
    assert(write_output_validation(prefix, saved) && saved.generation == 1);
    assert(read_output_validation(prefix, saved.aircraft, 2, loaded));
    assert(!std::memcmp(&loaded, &saved, sizeof(saved)));
    assert(!read_output_validation(prefix, "different aircraft", 2, loaded));
    assert(!read_output_validation(prefix, saved.aircraft, 1, loaded));
    auto changed = saved; changed.proofs[3] = 0; ++changed.generation;
    replace_slot(0, changed); // Crash halfway through revoking Mixture.
    assert(read_output_validation(prefix, saved.aircraft, 2, loaded));
    assert(loaded.proofs[0] == 100 && loaded.proofs[1] == 101 && loaded.proofs[2] == 102 && !loaded.proofs[3]);
    changed.proofs[3] = 999; replace_slot(0, changed); // Halfway through adding a different route proof.
    assert(read_output_validation(prefix, saved.aircraft, 2, loaded) && !loaded.proofs[3]);
    for (unsigned damage = 0; damage < 5; ++damage) {
        auto invalid = saved;
        if (damage == 0) invalid.magic = 0;
        if (damage == 1) invalid.version = 99;
        if (damage == 2) invalid.generation = 0;
        replace_slot(0, invalid, damage == 3, damage == 4);
        assert(!read_output_validation(prefix, saved.aircraft, 2, loaded));
    }
    assert(write_output_validation(prefix, saved));
    {
        char path[600]{}; std::snprintf(path, sizeof(path), "%s.1.bin", prefix);
        auto* file = std::fopen(path, "wb"); assert(file); assert(std::fputc(0, file) == 0); std::fclose(file);
        assert(!read_output_validation(prefix, saved.aircraft, 2, loaded)); // One valid copy cannot revive a revoked pass.
    }
    {
        output_validation_record_v1 legacy{}; legacy.generation=saved.generation;legacy.engines=saved.engines;
        std::memcpy(legacy.aircraft,saved.aircraft,sizeof(legacy.aircraft));std::memcpy(legacy.proofs,saved.proofs,sizeof(legacy.proofs));
        legacy.checksum=profile_hash(&legacy,offsetof(output_validation_record_v1,checksum));
        for(unsigned slot=0;slot<2;++slot){
            char path[600]{};std::snprintf(path,sizeof(path),"%s.%u.bin",prefix,slot);
            auto* file=std::fopen(path,"wb");assert(file);
            assert(std::fwrite(&legacy,1,sizeof(legacy),file)==sizeof(legacy));assert(!std::fclose(file));
        }
        assert(read_output_validation(prefix,saved.aircraft,2,loaded) && loaded.version==2 && !loaded.response_axes);
        for(unsigned i=0;i<4;++i)assert(loaded.proofs[i]==saved.proofs[i]); // Old engine checks survive format migration.
        auto alternate=saved;alternate.response_axes=1u<<4;++alternate.generation;replace_slot(0,alternate);
        assert(read_output_validation(prefix,saved.aircraft,2,loaded) && !loaded.proofs[0] && loaded.proofs[1]==saved.proofs[1]);
        saved.response_axes=1u<<4;assert(write_output_validation(prefix,saved));
        assert(read_output_validation(prefix,saved.aircraft,2,loaded) && loaded.response_axes==(1u<<4));
    }
    const auto generation = saved.generation;
    assert(!write_output_validation("artifacts/efb-control-test/tests/no-such-directory/cache", saved) && saved.generation == generation);
    saved.generation = UINT32_MAX; assert(!write_output_validation(prefix, saved));
    clean();
    std::cout << "Output validation storage passed: actual disk round-trip, aircraft/engine identity, independent agreeing copies, interrupted revocation, corrupt/truncated/extra data, format versions and write failures. No simulator access.\n";
}
