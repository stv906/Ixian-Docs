export interface Navigation {
  title: string
  href: string
  external?: boolean
}

export const Navigations: Navigation[] = [
  {
    title: "Operators",
    href: `/docs/operators`,
  },
  {
    title: "Developers",
    href: `/docs/developers`,
  },
  {
    title: "Architecture",
    href: `/docs/architecture`,
  },
  {
    title: "Reference",
    href: `/docs/reference`,
  },
]

export const GitHubLink = {
  href: "https://github.com/ixian-platform",
}

export const links = {
  ixian: "https://www.ixian.io/",
  ixiscope: "https://explorer.ixian.io/",
  spixi: "https://www.spixi.io/",
  downloads: "https://www.ixian.io/get-involved#resources",
}
