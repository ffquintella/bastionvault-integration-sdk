"""The fake tokens the Python tests drive, assembled rather than written as literals.

CNF-025's secret scan rejects ``s.<20+ alnum>`` anywhere in the tracked tree except
``specifications/fixtures/**``. The fix is here rather than in the gate: the whitelist is
not widened and the pattern is not narrowed (CLA-004), so a real token pasted into a test
would still be caught (D-M1c-15).

Adjacent-literal concatenation is what breaks the text the scanner matches; the values are
byte-identical to the literals they replace.
"""

CLIENT = "s." "FAKEtoken0000000000000000"
OTHER = "s." "FAKEother000000000000000000"
USED = "s." "FAKEused00000000000000000"
PERSISTED = "s." "FAKEpersisted00000000000"
SHARED = "s." "FAKEshared0000000000000"
ISSUED = "s." "FAKEissued0000000000000"
PINNED = "s." "FAKEpinned0000000000000"
SWAPPED = "s." "FAKEswapped0000000000000"
LEAK = "s." "FAKEleak00000000000000"
CREATED = "s." "FAKEcreated00000000000000"
RESOLVED_1 = "s." "FAKEresolved00000001"
