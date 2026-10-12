
import React from "react";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "../ui-kits/breadcrumb/breadcrumb";
import { Link } from "react-router";
import useRoutePathSegments from "@/hooks/use-path-segments";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";

/**
 * `customTitles` is keyed by the same absolute path the breadcrumb builds its hrefs from, and wins
 * over the shared `BREADCRUMB_CUSTOM_TITLES` map. Prefer it: the map is written by pages while they
 * render (or from an effect, which lands after this component has already read it), so a title that
 * arrives with fetched data cannot be shown reliably through it.
 */
const PageBreadcrumb: React.FC<{
  breadcrumbIndex?: number;
  customTitles?: Record<string, string | null>;
}> = ({ breadcrumbIndex, customTitles }) => {
  let breadcrumbs = useRoutePathSegments();
  if (breadcrumbIndex && breadcrumbIndex > 0) {
    breadcrumbs = breadcrumbs.slice(breadcrumbIndex - 1);
  }
  return (
    <Breadcrumb className="hidden md:flex">
      <BreadcrumbList>
        {breadcrumbs.map((breadcrumb, index) => (
          <React.Fragment key={breadcrumb.href}>
            <BreadcrumbItem>
              {index === breadcrumbs.length - 1 ? (
                <BreadcrumbPage className="text-low-emphasis">
                  {customTitles?.[breadcrumb.href] ||
                    BREADCRUMB_CUSTOM_TITLES[breadcrumb.href] ||
                    breadcrumb.label}
                </BreadcrumbPage>
              ) : (
                <BreadcrumbLink asChild>
                  <Link to={breadcrumb.href} className="text-foreground hover:text-foreground">
                    {customTitles?.[breadcrumb.href] ||
                      BREADCRUMB_CUSTOM_TITLES[breadcrumb.href] ||
                      breadcrumb.label}
                  </Link>
                </BreadcrumbLink>
              )}
            </BreadcrumbItem>
            {index < breadcrumbs.length - 1 && <BreadcrumbSeparator />}
          </React.Fragment>
        ))}
      </BreadcrumbList>
    </Breadcrumb>
  );
};

export default PageBreadcrumb;
