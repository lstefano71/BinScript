# Makefile — Build the BinScript C example
#
# Windows (MSVC):  nmake            (or just: cl /nologo /W3 parse_bmp.c)
# Linux/macOS:     make

ifeq ($(OS),Windows_NT)
    CC      = cl
    CFLAGS  = /nologo /W3
    OUT     = /Fe:parse_bmp.exe
    TARGET  = parse_bmp.exe
    CLEAN   = del /q parse_bmp.exe parse_bmp.obj roundtrip.bmp bmp.bsc 2>nul
    SAMPLE  = ..\..\..\tests\samples\bmp\4x4_24bit.bmp
else
    CC      = gcc
    CFLAGS  = -Wall -Wextra -O2
    LDFLAGS = -ldl
    OUT     = -o parse_bmp
    TARGET  = parse_bmp
    CLEAN   = rm -f parse_bmp roundtrip.bmp bmp.bsc
    SAMPLE  = ../../../tests/samples/bmp/4x4_24bit.bmp
endif

all: $(TARGET)

$(TARGET): parse_bmp.c
	$(CC) $(CFLAGS) parse_bmp.c $(OUT) $(LDFLAGS)

run: $(TARGET)
	./$(TARGET) $(SAMPLE)

clean:
	$(CLEAN)

.PHONY: all run clean
