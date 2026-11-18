// Populate the protocol's engine-pair fields for test fixtures. Explicit fields
// stay untouched so malformed-status checks still exercise the real validator.
function withPairs(value) {
    const pairs = ['throttle', 'propeller', 'mixture'];
    if (value.advanced === undefined) value.advanced = false;
    if (value.autoMask === undefined) value.autoMask = 0;
    pairs.forEach((group, index) => {
        if (value[group + 'Blocked'] === undefined) value[group + 'Blocked'] = value.quadrantBlocked;
        if (value[group] === undefined) value[group] = { seen: 0, blocked: 0, last: '', value: 0 };
        if (value.pickup && value.pickup.quadrant && !value.pickup[group]) value.pickup[group] = {
            waiting: value.pickup.quadrant.waiting, tolerance: value.pickup.quadrant.tolerance,
            axes: value.pickup.quadrant.axes.filter(axis => axis.id.startsWith(group))
        };
        if (value.pickup && value.pickup[group] && value.pickup[group].autoEnabled === undefined)
            value.pickup[group].autoEnabled = !!(value.autoMask & (2 << index));
    });
    if (value.quadrantMixed === undefined) value.quadrantMixed = pairs.some(group => value[group + 'Blocked']) && !pairs.every(group => value[group + 'Blocked']);
    if (value.quadrantToleranceMixed === undefined) value.quadrantToleranceMixed = false;
    if (value.pickup) ['steering', 'quadrant'].forEach(group => {
        if (value.pickup[group] && value.pickup[group].autoEnabled === undefined) value.pickup[group].autoEnabled = group === 'quadrant' && !!value.autoMask;
    });
    return value;
}
module.exports = withPairs;
