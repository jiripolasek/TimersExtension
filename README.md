# Timers website

A static Astro site for Timers for PowerToys Command Palette. The `pages` branch
has its own history and uses the
[Media Controls Pages setup](https://github.com/jiripolasek/MediaControlsExtension/tree/pages).

## Develop

Use Node.js 22.23.4 or later. The `.nvmrc` pins Node.js 24.

```powershell
npm ci
npm run dev
npm run check
npm run build
npm run preview
```

Open the Local URL printed by Astro. The default is
`http://127.0.0.1:4321/TimersExtension/`. The static build is in `dist/`.

## Hosting

The repository is configured for GitHub Actions deployment at
`https://timers.jiripolasek.com/`. The
[workflow](.github/workflows/pages.yml) checks and builds the site on Node.js 24,
then deploys on pushes to `pages`. It passes the configured Pages host and base
path to Astro. The local build defaults to the GitHub project URL
`https://jiripolasek.github.io/TimersExtension/`. Set `SITE_URL` and `BASE_PATH`
to build for another host or path.

## Content and artwork

- `src/pages/` contains the landing, screenshots, support, and privacy pages.
- `src/content/privacy.md` is the privacy text used by the site.
- `src/data/site.ts` contains the shared links and WinGet package ID.
- `src/assets/` contains the promo captures and hero wallpaper. The site crops
  the captures at their original scale and links to the full PNGs.
- `public/images/social.png` is the captured social card.
- The interface uses [Microsoft Fluent System Icons](https://github.com/microsoft/fluentui-system-icons)
  under the [MIT license](public/licenses/fluent-system-icons.txt).
