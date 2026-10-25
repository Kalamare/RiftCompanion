#pragma once
#include <memory>
#include <string>

// Synthetic pixels only. A WPF producer can later provide bounded BGRA frames.
struct ProbeBridge {
    virtual ~ProbeBridge() = default;
    virtual void update(unsigned seconds) = 0;
    virtual void toggle() = 0;
    virtual std::wstring status() const = 0;
    virtual bool connected() const = 0;
};
#ifdef WITH_GOVERLAY
std::unique_ptr<ProbeBridge> startBridge();
#endif
