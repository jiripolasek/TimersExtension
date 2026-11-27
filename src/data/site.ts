const repository = "https://github.com/jiripolasek/TimersExtension";

export const site = {
  name: "Timers for Command Palette",
  description: "Run countdowns, track stopwatch laps, and keep both in view on the PowerToys Command Palette Dock.",
  repository,
  issues: `${repository}/issues`,
  releases: `${repository}/releases`,
  author: "https://jiripolasek.com",
  powerToys: "https://apps.microsoft.com/detail/XP89DCGQ3K6VLD",
  dockGuide: "https://learn.microsoft.com/en-us/windows/powertoys/command-palette/dock",
  winget: "winget install -e --id JiriPolasek.TimersforCommandPalette",
};

export function withBase(path = "") {
  return `${import.meta.env.BASE_URL.replace(/\/?$/, "/")}${path.replace(/^\/+/, "")}`;
}
