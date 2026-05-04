import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui-kits/accordion/accordion";
import { Separator } from "@/components/ui-kits/separator/separator";

import { formatDate, parseDateString } from "@/lib/utils";
import { useContext } from "react";

import AnnotationBar from "../annotation-bar/annotation-bar";
import { timelineContext } from "../trace-details";

export const TracingInfo = () => {
  const { selectedTrace: trace } = useContext(timelineContext);
  if (!trace) return null;
  return (
    <>
      <p className="break-all text-lg font-normal text-high-emphasis md:text-xl">
        {trace?.entryPoint?.method} {trace?.entryPoint?.actionName}
      </p>
      <div className="mt-[12px] grid grid-cols-2 gap-4">
        <div>
          <h3 className="text-sm font-medium text-low-emphasis">Status</h3>
          <p className="break-all text-base font-normal text-high-emphasis">{trace?.status}</p>
        </div>
        <div>
          <h3 className="text-sm font-medium text-low-emphasis">Kind</h3>
          <p className="break-all text-base font-normal text-high-emphasis">{trace?.kind}</p>
        </div>
        <div>
          <h3 className="text-sm font-medium text-low-emphasis">Service</h3>
          <p className="break-all text-base font-normal text-high-emphasis">{trace?.serviceName}</p>
        </div>
        <div>
          <h3 className="text-sm font-medium text-low-emphasis">Parent ID</h3>
          <p className="break-all text-base font-normal text-high-emphasis">{trace?.parentId}</p>
        </div>
        <div>
          <h3 className="text-sm font-medium text-low-emphasis">Span ID</h3>
          <p className="break-all text-base font-normal text-high-emphasis">{trace?.spanId}</p>
        </div>
        <div>
          <h3 className="text-sm font-medium text-low-emphasis">Span name</h3>
          <p className="break-all text-base font-normal text-high-emphasis">
            {trace?.entryPoint?.actionName}
          </p>
        </div>
      </div>
      <Separator className="my-5" />
      <Accordion type="multiple">
        <AccordionItem value="annotation" className="last:border-none">
          <AccordionTrigger className="font-medium hover:no-underline">Annotation</AccordionTrigger>
          <AccordionContent>
            <div className="flex flex-col py-[12px]">
              <div className="mb-[12px]">
                <AnnotationBar annotationDuration={trace?.duration} />
              </div>
              <div className="mb-[12px] grid grid-cols-2 gap-2">
                <div className="flex items-center">0ms</div>
                <div className="flex flex-col">
                  <div className="mb-[8px]">
                    <h3 className="text-sm font-medium text-low-emphasis">Start time</h3>
                    <p className="text-base font-normal text-high-emphasis">
                      {formatDate(parseDateString(trace?.startTime?.toString()))}
                    </p>
                  </div>
                  <div className="mb-[8px]">
                    <h3 className="text-sm font-medium text-low-emphasis">Value</h3>
                    <p className="text-base font-normal text-high-emphasis">Server Start</p>
                  </div>
                  <div>
                    <h3 className="text-sm font-medium text-low-emphasis">Address</h3>
                    <p className="text-base font-normal text-high-emphasis">
                      {trace?.entryPoint?.method}
                    </p>
                  </div>
                </div>
              </div>
              <Separator />
              <div className="mt-[12px] grid grid-cols-2 gap-2">
                <div className="flex items-center">{trace?.duration}ms</div>
                <div className="flex flex-col">
                  <div className="mb-[8px]">
                    <h3 className="text-sm font-medium text-low-emphasis">Start time</h3>
                    <p className="text-base font-normal text-high-emphasis">
                      {formatDate(parseDateString(trace?.endTime?.toString()))}
                    </p>
                  </div>
                  <div className="mb-[8px]">
                    <h3 className="text-sm font-medium text-low-emphasis">Value</h3>
                    <p className="text-base font-normal text-high-emphasis">Server Finish</p>
                  </div>
                  <div>
                    <h3 className="text-sm font-medium text-low-emphasis">Address</h3>
                    <p className="text-base font-normal text-high-emphasis">
                      {trace?.entryPoint?.method}
                    </p>
                  </div>
                </div>
              </div>
            </div>
          </AccordionContent>
        </AccordionItem>
        {Object.keys(trace.attributes).length > 0 && (
          <AccordionItem value="attributes" className="last:border-none">
            <AccordionTrigger className="font-medium hover:no-underline">
              Attributes
            </AccordionTrigger>
            <AccordionContent className="flex flex-col">
              <div className="flex flex-col gap-3">
                {Object.keys(trace.attributes)
                  .filter((item) => item.toLowerCase() !== "dbname")
                  .map((item) => (
                    <div key={item}>
                      <h3 className="text-sm font-medium capitalize text-low-emphasis">
                        {item.split(".").join(" ")}
                      </h3>
                      <p className="text-base font-normal text-high-emphasis">
                        {trace?.attributes[item] || "-"}
                      </p>
                    </div>
                  ))}
              </div>
            </AccordionContent>
          </AccordionItem>
        )}
      </Accordion>
    </>
  );
};
