# Fixture policy

Fixtures must be copied into unique temporary test directories before mutation. The local Hero Team Wars source map is an integration input, not a writable fixture, and is never copied over by the test suite.

The checked-in JSON expectations include the MVP and full six-team capability
profiles. Binary map fixtures remain generated in unique temporary directories
from the immutable local source because the source map is not distributable.
Expected future fixture classes include a valid tiny map, truncated archive,
unknown opaque member, duplicate rawcode, invalid reference, out-of-bounds
region, and disconnected script/import case. Each fixture must record its
origin, distribution rule, expected capability, and SHA-256 before it is added.
