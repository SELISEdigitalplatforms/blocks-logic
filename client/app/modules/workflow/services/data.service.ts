import {
  IGetSchemaDetailsResponse,
  IGetSchemaListPayload,
  IGetSchemaListResponse,
} from "../models/data-service";
import { serviceInstances } from "@/lib/http-client";
import { SCHEMA_ENDPOINTS } from "../constants/data.endpoint.constant";

class DataService {
  private readonly DataHttpClient = serviceInstances.dataService;

  getSchemaList(
    payload: IGetSchemaListPayload,
  ): Promise<IGetSchemaListResponse> {
    const url = `${SCHEMA_ENDPOINTS.LIST}?Keyword=${payload.keyword}&PageSize=${payload.pageSize}&PageNo=${payload.pageNo}&SortDescending=${payload.sortDescending}&SortBy=${payload.sortBy}&SchemaType=${payload.schemaType}`;
    return this.DataHttpClient.get(url, undefined, { absoluteUrl: true });
  }

  getSchemaDetails(id: string): Promise<IGetSchemaDetailsResponse> {
    const params = new URLSearchParams({ id });
    const url = `${SCHEMA_ENDPOINTS.DETAILS}?${params.toString()}`;
    return this.DataHttpClient.get(
      url,
      undefined,
      { absoluteUrl: true },
    );
  }
}

export const dataService = new DataService();
