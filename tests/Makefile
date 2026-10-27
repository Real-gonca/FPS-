# PrecisionCurve - host test suite
#
#   make -C tests          build and run everything
#   make -C tests clean
#
# Nothing here needs Windows, the WDK or a certificate: the driver sources are
# compiled against tests/host/include/wdk_shim.h and executed in user space.

CC      ?= cc
CFLAGS  ?= -std=c11 -O2 -Wall -Wextra
CFLAGS  += -Wno-unused-function -Wno-unused-parameter -Wno-unknown-pragmas -Wno-unused-value
LDLIBS  ?= -lm
BUILD   := build
SHIM    := -Ihost/include

DRIVER_SRC := ../driver/Driver.c ../driver/Intercept.c ../driver/Config.c
SHIM_SRC   := host/shim.c

.PHONY: all test math parity driver bench clean

all: test

test: math parity driver
	@echo
	@echo "All host test suites passed."

math: $(BUILD)/test_math
	@echo "== shared math =="
	@./$(BUILD)/test_math

parity: $(BUILD)/gen_vectors
	@echo "== language parity =="
	@./$(BUILD)/gen_vectors > parity/vectors.json
	@node parity/parity.js

driver: $(BUILD)/test_driver
	@echo "== driver simulation =="
	@./$(BUILD)/test_driver

$(BUILD):
	@mkdir -p $(BUILD)

$(BUILD)/test_math: test_math.c ../shared/precisioncurve_math.h ../shared/precisioncurve_curves.h | $(BUILD)
	$(CC) $(CFLAGS) -o $@ test_math.c $(LDLIBS)

$(BUILD)/gen_vectors: parity/gen_vectors.c ../shared/precisioncurve_curves.h | $(BUILD)
	$(CC) $(CFLAGS) -o $@ parity/gen_vectors.c $(LDLIBS)

# The driver sources are compiled UNMODIFIED; only the WDK headers are shimmed.
$(BUILD)/test_driver: host/test_driver.c $(SHIM_SRC) $(DRIVER_SRC) | $(BUILD)
	$(CC) $(CFLAGS) $(SHIM) -o $@ host/test_driver.c $(SHIM_SRC) $(DRIVER_SRC)

clean:
	rm -rf $(BUILD) parity/vectors.json

# Hot path benchmark (not part of `make test`: timing on a shared CI box is noise).
bench: $(BUILD)/bench_driver
	@./$(BUILD)/bench_driver

$(BUILD)/bench_driver: host/bench_driver.c $(SHIM_SRC) $(DRIVER_SRC) | $(BUILD)
	$(CC) $(CFLAGS) $(SHIM) -o $@ host/bench_driver.c $(SHIM_SRC) $(DRIVER_SRC)
