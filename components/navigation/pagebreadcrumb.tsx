import { Fragment } from "react"

import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb"

import searchData from "@/public/search-data/documents.json"

export default function PageBreadcrumb({ paths }: { paths: string[] }) {
  return (
    <div className="pb-5">
      <Breadcrumb>
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink>Docs</BreadcrumbLink>
          </BreadcrumbItem>

          {paths.map((path, index) => {
            const href = `/docs/${paths.slice(0, index + 1).join("/")}`

            return (
              <Fragment key={path}>
                <BreadcrumbSeparator />
                <BreadcrumbItem>
                  {index < paths.length - 1 ? (
                    <BreadcrumbLink href={href} className="a">
                      {toTitleCase(path, href)}
                    </BreadcrumbLink>
                  ) : (
                    <BreadcrumbPage className="b">
                      {toTitleCase(path, href)}
                    </BreadcrumbPage>
                  )}
                </BreadcrumbItem>
              </Fragment>
            )
          })}
        </BreadcrumbList>
      </Breadcrumb>
    </div>
  )
}

function toTitleCase(input: string, href: string): string {
  const page = searchData.find((x) => "/docs" + x.slug === href)
  if (page) {
    return page.title
  }
  const words = input.split("-")
  const capitalizedWords = words.map(
    (word) => word.charAt(0).toUpperCase() + word.slice(1)
  )
  return capitalizedWords.join(" ")
}
